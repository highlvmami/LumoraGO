using System.Globalization;
using System.Text.RegularExpressions;
using LumoraGo.Core.Info;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Sözlük ve anlık bilgi kartları: "/tanım", "/hava", "/hisse", "/mac" ve uçuş kodu satırı.</summary>
public static partial class InfoPlugins
{
    public const string EmojiPrefix = EmojiPlugin.IconPrefix;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<PluginEntry> Create(
        DictionaryLookup dictionary,
        WeatherService weather,
        QuoteService quotes,
        FootballService football,
        Func<string> weatherCity,
        Action<string> open)
    {
        LiveLookup define = (arg, host, ct) => DefineAsync(dictionary, arg, host, open, ct);
        LiveLookupPlugin[] live =
        [
            new LiveLookupPlugin(new PluginMetadata("lumorago.dictionary", "Sözlük", "/tanım"), "Örnek: /tanım kalem, /tanım en serendipity", define,
                cacheFor: TimeSpan.FromDays(1)),
            new LiveLookupPlugin(new PluginMetadata("lumorago.dictionary.ascii", "Sözlük", "/tanim"), "Örnek: /tanim kalem, /tanim en serendipity", define,
                cacheFor: TimeSpan.FromDays(1)),
            new LiveLookupPlugin(new PluginMetadata("lumorago.weather", "Hava durumu", "/hava"), "Örnek: /hava, /hava izmir, /hava yarın",
                (arg, _, ct) => WeatherAsync(weather, weatherCity(), arg, open, ct), needsArgument: false, cacheFor: TimeSpan.FromMinutes(15)),
            new LiveLookupPlugin(new PluginMetadata("lumorago.quote", "Hisse ve kripto", "/hisse"), "Örnek: /hisse THYAO, /hisse AAPL, /hisse btc",
                (arg, host, ct) => QuotesAsync(quotes, arg, host, open, ct), cacheFor: TimeSpan.FromMinutes(2)),
            new LiveLookupPlugin(new PluginMetadata("lumorago.football", "Maç skoru", "/mac"), "Örnek: /mac fenerbahçe; boş bırakırsan Süper Lig haftası",
                (arg, _, ct) => MatchesAsync(football, arg, open, ct), needsArgument: false, cacheFor: TimeSpan.FromMinutes(1)),
        ];
        return [.. live.Select(p => new PluginEntry(p.Metadata, p)), new PluginEntry(FlightPlugin.Metadata, new FlightPlugin(open))];
    }

    // ---- Sözlük ----

    internal static async Task<IReadOnlyList<Result>> DefineAsync(DictionaryLookup dictionary, string argument, IPluginHost? host, Action<string> open, CancellationToken ct)
    {
        var english = argument.StartsWith("en ", StringComparison.OrdinalIgnoreCase);
        var word = english ? argument[3..].Trim() : argument;
        var entry = english ? await dictionary.EnglishAsync(word, ct) : await dictionary.TurkishAsync(word, ct);
        if (entry is null && !english && word.All(c => c < 128))
        {
            entry = await dictionary.EnglishAsync(word, ct);
        }

        if (entry is null)
        {
            return [];
        }

        var preview = entry.FullText();
        var results = new List<Result>();
        for (var i = 0; i < entry.Senses.Count && i < 8; i++)
        {
            var sense = entry.Senses[i];
            var parts = new List<string> { $"{i + 1}." };
            if (sense.Kind is { Length: > 0 } kind)
            {
                parts.Add(kind);
            }

            if (sense.Example is { Length: > 0 } example)
            {
                parts.Add($"“{example}”");
            }

            results.Add(new Result(sense.Meaning, string.Join(" ", parts) + " · Enter ile kopyala", null, 900 - i, _ =>
            {
                host?.CopyToClipboard(sense.Meaning);
                return true;
            })
            {
                PreviewText = preview,
                AutoComplete = entry.Word,
            });
        }

        if (entry.Synonyms.Count > 0)
        {
            var synonyms = string.Join(", ", entry.Synonyms);
            results.Add(new Result($"Eş anlamlılar: {synonyms}", "Enter ile kopyala", null, 500, _ =>
            {
                host?.CopyToClipboard(synonyms);
                return true;
            }) { PreviewText = preview });
        }

        foreach (var (phrase, i) in entry.Phrases.Take(5).Select((p, i) => (p, i)))
        {
            results.Add(new Result(phrase, "Atasözü / deyim · Enter ile kopyala", null, 400 - i, _ =>
            {
                host?.CopyToClipboard(phrase);
                return true;
            }) { PreviewText = preview });
        }

        results.Add(new Result($"\"{entry.Word}\" için web'de bak", entry.Source, null, 10, _ =>
        {
            open(entry.Url);
            return true;
        }));
        return results;
    }

    // ---- Hava ----

    internal static async Task<IReadOnlyList<Result>> WeatherAsync(WeatherService weather, string defaultCity, string argument, Action<string> open, CancellationToken ct)
    {
        var words = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var mode = words.Count > 0 ? FuzzyMatcher.Fold(words[^1]) : "";
        if (mode is "yarin" or "bugun" or "hafta")
        {
            words.RemoveAt(words.Count - 1);
        }
        else
        {
            mode = "bugun";
        }

        var city = words.Count > 0 ? string.Join(' ', words) : defaultCity.Trim();
        var place = city.Length > 0 ? await weather.FindAsync(city, ct) : await weather.HereAsync(ct);
        if (place is null)
        {
            return [];
        }

        var report = await weather.ForecastAsync(place, ct);
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString($"{place.Name} hava durumu");
        bool Open(ActionContext _)
        {
            open(url);
            return true;
        }

        var results = new List<Result>();
        if (mode == "yarin" && report.Days.Count > 1)
        {
            var day = report.Days[1];
            var (text, emoji) = WeatherService.Describe(day.Code);
            results.Add(new Result($"{place.Label} · yarın", null, EmojiPrefix + emoji, 1000, Open)
            {
                Card = new ResultCard($"{Deg(day.Max)} / {Deg(day.Min)}", text, [Rain(day.RainChance)]),
            });
        }
        else
        {
            var (text, emoji) = WeatherService.Describe(report.Code);
            var lines = new List<string>();
            if (report.Days.Count > 0)
            {
                var today = report.Days[0];
                lines.Add($"Bugün en yüksek {Deg(today.Max)}, en düşük {Deg(today.Min)} · {Rain(today.RainChance)}");
            }

            lines.Add($"Rüzgâr {Math.Round(report.Wind).ToString(Turkish)} km/s · sonraki 12 saat sağda");
            results.Add(new Result($"{place.Label} · şu an", null, EmojiPrefix + emoji, 1000, Open)
            {
                Card = new ResultCard(Deg(report.Temperature), $"{text} · Hissedilen {Deg(report.FeelsLike)}", lines, report.Hourly),
            });
        }

        var shownDays = mode == "hafta" ? report.Days.Skip(1) : report.Days.Skip(mode == "yarin" ? 2 : 1).Take(2);
        foreach (var (day, i) in shownDays.Select((d, i) => (d, i)))
        {
            var (text, emoji) = WeatherService.Describe(day.Code);
            var name = day.Date == DateOnly.FromDateTime(DateTime.Today).AddDays(1) ? "Yarın" : day.Date.ToString("dddd d MMM", Turkish);
            results.Add(new Result($"{name}: {Deg(day.Max)} / {Deg(day.Min)} · {text}", Rain(day.RainChance), EmojiPrefix + emoji, 900 - i, Open));
        }

        return results;
    }

    private static string Deg(double value) => double.IsNaN(value) ? "?" : $"{Math.Round(value).ToString(Turkish)}°";

    private static string Rain(int? chance) => chance is { } c ? $"Yağış olasılığı %{c}" : "Yağış bilgisi yok";

    // ---- Hisse / kripto ----

    internal static async Task<IReadOnlyList<Result>> QuotesAsync(QuoteService quotes, string argument, IPluginHost? host, Action<string> open, CancellationToken ct)
    {
        var symbols = argument.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var found = await Task.WhenAll(symbols.Select(s => quotes.GetAsync(s, ct)));
        var results = new List<Result>();
        foreach (var (quote, i) in found.Select((q, i) => (q, i)))
        {
            if (quote is null)
            {
                results.Add(new Result($"\"{symbols[i]}\" bulunamadı", "BIST, ABD hisse kodu ya da btc, eth gibi kripto kısaltması yaz", null, 1, _ => false));
                continue;
            }

            var price = QuoteService.Money(quote.Price, quote.Currency);
            var caption = new List<string>();
            if (quote.ChangePercent is { } pct && quote.Change is { } change)
            {
                caption.Add($"{(pct >= 0 ? "+" : "")}%{pct.ToString("0.00", Turkish)} ({(change >= 0 ? "+" : "")}{change.ToString(Math.Abs(change) < 1 ? "0.####" : "N2", Turkish)})");
            }

            if (quote.Exchange is { Length: > 0 } exchange)
            {
                caption.Add(exchange);
            }

            var lines = new List<string>();
            if (quote.Note is { } note)
            {
                lines.Add(note);
            }

            lines.Add(quote.Delayed ? "Yaklaşık 15 dk gecikmeli · Yahoo Finance" : "Son 24 saatteki değişim");
            results.Add(new Result($"{quote.Name} ({quote.Symbol})", null, null, 1000 - i, _ =>
            {
                open(quote.Url);
                return true;
            },
            [
                new ContextAction("Sayfasını aç", _ =>
                {
                    open(quote.Url);
                    return true;
                }),
                new ContextAction("Fiyatı kopyala", _ =>
                {
                    host?.CopyToClipboard(price);
                    return true;
                }),
            ])
            {
                Card = new ResultCard(price, string.Join(" · ", caption), lines, quote.Points.Count > 1 ? quote.Points : null, quote.Change is { } c ? c >= 0 : null),
            });
        }

        return results;
    }

    // ---- Maç ----

    internal static async Task<IReadOnlyList<Result>> MatchesAsync(FootballService football, string team, Action<string> open, CancellationToken ct)
    {
        var matches = await football.FindAsync(team, ct);
        var asCards = team.Trim().Length > 0;
        return matches.Take(12).Select((m, i) =>
        {
            var score = m.HomeScore is { } h && m.AwayScore is { } a ? $"{h} – {a}" : "–";
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString($"{m.Home} {m.Away}");
            bool Open(ActionContext _)
            {
                open(url);
                return true;
            }

            return asCards && i < 3
                ? new Result(m.League, null, EmojiPrefix + "⚽", 1000 - i, Open)
                {
                    Card = new ResultCard($"{m.Home}  {score}  {m.Away}", m.Status, null, null, m.State == MatchState.Live ? true : null),
                }
                : new Result($"{m.Home}  {score}  {m.Away}", $"{m.Status} · {m.League}", EmojiPrefix + "⚽", 1000 - i, Open);
        }).ToList();
    }

    // ---- Uçuş ----

    /// <summary>"TK1980" gibi bir uçuş kodu yazılınca takip sayfasını açan satır. Ücretsiz ve güvenilir anlık kaynak olmadığı için kart yok.</summary>
    public sealed partial class FlightPlugin(Action<string> open) : IPlugin
    {
        public static readonly PluginMetadata Metadata = new("lumorago.flight", "Uçuş takibi");

        public void Init(PluginContext context)
        {
        }

        public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
        {
            if (FlightCode().Match(query.Search.Trim()) is not { Success: true } m)
            {
                return Task.FromResult<IReadOnlyList<Result>>([]);
            }

            var code = (m.Groups[1].Value + m.Groups[2].Value).ToUpperInvariant();
            return Task.FromResult<IReadOnlyList<Result>>(
            [
                new Result($"{code} uçuşunu takip et", "Flightradar24 · Ctrl+Enter FlightAware", EmojiPrefix + "✈️", 5, ctx =>
                {
                    open(ctx.Modifiers.HasFlag(ActionModifiers.Ctrl)
                        ? $"https://www.flightaware.com/live/flight/{code}"
                        : $"https://www.flightradar24.com/data/flights/{code.ToLowerInvariant()}");
                    return true;
                }),
            ]);
        }

        [GeneratedRegex(@"^([A-Za-z]{2}|[A-Za-z]\d|\d[A-Za-z])\s?(\d{2,4})$")]
        private static partial Regex FlightCode();
    }
}
