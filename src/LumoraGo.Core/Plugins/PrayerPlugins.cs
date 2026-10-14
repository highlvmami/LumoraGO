using System.Globalization;
using LumoraGo.Core.Info;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/ezan" namaz vakitleri ve sıradaki vakte kalan süre; "/eczane" nöbetçi eczane bağlantıları.</summary>
public static class PrayerPlugins
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<PluginEntry> Create(PrayerTimesService prayers, Func<string> city, Action<string> open, Func<DateTime>? now = null)
    {
        var clock = now ?? (() => DateTime.Now);
        LiveLookup lookup = (arg, _, ct) => PrayerAsync(prayers, arg, city(), open, clock(), ct);
        var plugins = new[] { "/ezan", "/namaz", "/vakit" }.Select(keyword =>
            new LiveLookupPlugin(new PluginMetadata("lumorago.prayer." + keyword.Trim('/'), "Namaz vakitleri", keyword),
                "Örnek: /ezan, /ezan ankara, /ezan yarın", lookup, needsArgument: false, debounce: TimeSpan.FromMilliseconds(300),
                cacheFor: TimeSpan.FromSeconds(30)));
        var pharmacy = new PharmacyPlugin(city, open);
        return [.. plugins.Select(p => new PluginEntry(p.Metadata, p)), new PluginEntry(pharmacy.Metadata, pharmacy)];
    }

    internal static async Task<IReadOnlyList<Result>> PrayerAsync(PrayerTimesService prayers, string argument, string defaultCity,
        Action<string> open, DateTime now, CancellationToken ct)
    {
        var words = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var tomorrow = words.Count > 0 && FuzzyMatcher.Fold(words[^1]) == "yarin";
        if (tomorrow)
        {
            words.RemoveAt(words.Count - 1);
        }

        var city = words.Count > 0 ? string.Join(' ', words) : defaultCity.Trim().Length > 0 ? defaultCity.Trim() : "İstanbul";
        var today = DateOnly.FromDateTime(now);
        var day = await prayers.GetAsync(city, tomorrow ? today.AddDays(1) : today, ct);
        if (day is null)
        {
            return [new Result($"\"{city}\" için vakit bulunamadı", "Şehir adını kontrol et, örn. /ezan izmir", null, 1, _ => false)];
        }

        var url = "https://namazvakitleri.diyanet.gov.tr/tr-TR";
        bool Open(ActionContext _)
        {
            open(url);
            return true;
        }

        var lines = day.Times.Select(t => $"{t.Name}  {t.Time:HH\\:mm}").ToList();
        string headline;
        string caption;
        if (tomorrow)
        {
            headline = $"Yarın imsak {day.Times[0].Time:HH\\:mm}";
            caption = $"{day.City} · {day.Date.ToString("d MMMM dddd", Turkish)}";
        }
        else
        {
            var time = TimeOnly.FromDateTime(now);
            var nextIndex = day.Times.ToList().FindIndex(t => t.Time > time);
            (string Name, DateTime At) next;
            if (nextIndex >= 0)
            {
                next = (day.Times[nextIndex].Name, day.Date.ToDateTime(day.Times[nextIndex].Time));
                lines[nextIndex] += "  ← sıradaki";
            }
            else
            {
                var after = await prayers.GetAsync(city, today.AddDays(1), ct);
                var imsak = after?.Times[0].Time ?? day.Times[0].Time;
                next = ("İmsak", today.AddDays(1).ToDateTime(imsak));
            }

            headline = $"{Suffix(next.Name)} {Remaining(next.At - now)}";
            caption = $"{day.City} · {next.Name} {next.At:HH\\:mm} · {day.Date.ToString("d MMMM dddd", Turkish)}";
        }

        var results = new List<Result>
        {
            new($"{day.City} namaz vakitleri", null, InfoPlugins.EmojiPrefix + "🕌", 1000, Open)
            {
                Card = new ResultCard(headline, caption, lines),
                PreviewText = string.Join(Environment.NewLine, lines),
            },
        };
        results.AddRange(day.Times.Select((t, i) =>
            new Result($"{t.Name} {t.Time:HH\\:mm}", day.City, null, 900 - i, ctx => Open(ctx))));
        return results;
    }

    /// <summary>"Akşam" → "Akşama", "İkindi" → "İkindiye", "Öğle" → "Öğleye".</summary>
    internal static string Suffix(string name) => name switch
    {
        "İmsak" => "İmsaka",
        "Güneş" => "Güneşe",
        "Öğle" => "Öğleye",
        "İkindi" => "İkindiye",
        "Akşam" => "Akşama",
        "Yatsı" => "Yatsıya",
        _ => name,
    };

    internal static string Remaining(TimeSpan span)
    {
        var minutes = Math.Max(0, (int)Math.Ceiling(span.TotalMinutes));
        return minutes >= 60 ? $"{minutes / 60} sa {minutes % 60} dk" : $"{minutes} dk";
    }
}

/// <summary>
/// "/eczane kadıköy": nöbetçi eczaneleri gösteren resmi ve harita sayfalarını açar. Türkiye geneli için herkese açık bir
/// veri kaynağı yok (siteler bot korumalı), bu yüzden listeyi tarayıcıda gösterir.
/// </summary>
public sealed class PharmacyPlugin(Func<string> city, Action<string> open) : IPlugin
{
    public const string Keyword = "/eczane";

    public PluginMetadata Metadata { get; } = new("lumorago.pharmacy", "Nöbetçi eczane", Keyword);

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var place = query.Search.Trim().Length > 0 ? query.Search.Trim() : city().Trim();
        var near = place.Length > 0 ? place : "yakınımdaki";
        var results = new List<Result>
        {
            Link($"Nöbetçi eczaneler: {near}", "Google Haritalar'da açık olanlar, yol tarifiyle", "🗺",
                "https://www.google.com/maps/search/" + Uri.EscapeDataString($"nöbetçi eczane {place}".Trim()), 1000),
            Link("e-Devlet nöbetçi eczane sorgulama", "Sağlık Bakanlığı (TİTCK) resmi listesi; il ve ilçeyi seç", "🏛",
                "https://www.turkiye.gov.tr/saglik-titck-nobetci-eczane-sorgulama", 900),
            Link($"Google'da ara: nöbetçi eczane {place}".TrimEnd(), "Eczacı odalarının güncel listeleri", "🔎",
                "https://www.google.com/search?q=" + Uri.EscapeDataString($"nöbetçi eczane {place}".Trim()), 800),
        };
        if (FuzzyMatcher.Fold(place).Contains("istanbul", StringComparison.Ordinal) || IstanbulDistrict(place))
        {
            results.Insert(1, Link("İstanbul Eczacı Odası nöbetçi listesi", "İstanbul ve Yalova, harita ile", "💊",
                "https://www.istanbuleczaciodasi.org.tr/nobetci-eczane/", 950));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static readonly string[] IstanbulDistricts =
    [
        "adalar", "arnavutkoy", "atasehir", "avcilar", "bagcilar", "bahcelievler", "bakirkoy", "basaksehir", "bayrampasa", "besiktas",
        "beykoz", "beylikduzu", "beyoglu", "buyukcekmece", "catalca", "cekmekoy", "esenler", "esenyurt", "eyupsultan", "fatih",
        "gaziosmanpasa", "gungoren", "kadikoy", "kagithane", "kartal", "kucukcekmece", "maltepe", "pendik", "sancaktepe", "sariyer",
        "silivri", "sultanbeyli", "sultangazi", "sile", "sisli", "tuzla", "umraniye", "uskudar", "zeytinburnu",
    ];

    private static bool IstanbulDistrict(string place) =>
        place.Length > 0 && IstanbulDistricts.Contains(FuzzyMatcher.Fold(place.Split(' ')[0]));

    private Result Link(string title, string subtitle, string emoji, string url, int score) =>
        new(title, subtitle, InfoPlugins.EmojiPrefix + emoji, score, _ =>
        {
            open(url);
            return true;
        });
}
