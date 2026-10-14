using System.Globalization;
using LumoraGo.Core.Info;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>İnternetten bilgi: "/haber" (RSS başlıkları), "/film" ve "/dizi" (TMDB), "/çevir" (Google Çeviri).</summary>
public static class WebInfoPlugins
{
    private const int MaxNews = 30;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<PluginEntry> Create(
        NewsService news,
        Func<IReadOnlyList<NewsFeed>> feeds,
        MovieService movies,
        Translator translator,
        Func<string?> clipboard,
        Action<string> paste,
        Action<string> open,
        Action openSettings)
    {
        LiveLookup newsLookup = (arg, _, ct) => NewsAsync(news, feeds(), arg, open, DateTime.Now, ct);
        LiveLookup movieLookup = (arg, _, ct) => MoviesAsync(movies, arg, open, openSettings, ct);
        LiveLookup translate = (arg, host, ct) => TranslateAsync(translator, arg, clipboard, host, paste, open, ct);
        LiveLookupPlugin[] plugins =
        [
            new(new PluginMetadata("lumorago.news", "Haberler", "/haber"), "Örnek: /haber, /haber bbc, /haber deprem", newsLookup,
                needsArgument: false, cacheFor: TimeSpan.FromMinutes(2)),
            new(new PluginMetadata("lumorago.movie", "Film ve dizi", "/film"), "Örnek: /film inception, /film yeşilçam", movieLookup,
                cacheFor: TimeSpan.FromHours(6)),
            new(new PluginMetadata("lumorago.movie.tv", "Film ve dizi", "/dizi"), "Örnek: /dizi breaking bad", movieLookup,
                cacheFor: TimeSpan.FromHours(6)),
            new(new PluginMetadata("lumorago.translate.inline", "Çeviri", "/çevir"), "", translate,
                needsArgument: false, debounce: TimeSpan.FromMilliseconds(600), cacheFor: TimeSpan.FromSeconds(5)),
            new(new PluginMetadata("lumorago.translate.inline.ascii", "Çeviri", "/cevir"), "", translate,
                needsArgument: false, debounce: TimeSpan.FromMilliseconds(600), cacheFor: TimeSpan.FromSeconds(5)),
        ];
        return plugins.Select(p => new PluginEntry(p.Metadata, p)).ToList();
    }

    // ---- Haber ----

    internal static async Task<IReadOnlyList<Result>> NewsAsync(
        NewsService news, IReadOnlyList<NewsFeed> feeds, string argument, Action<string> open, DateTime now, CancellationToken ct)
    {
        if (feeds.Count == 0)
        {
            return [new Result("Haber kaynağı yok", "Ayarlar → İnternet'ten RSS adresi ekle", null, 1, _ => false)];
        }

        var filter = FuzzyMatcher.Fold(argument.Trim());
        var sources = filter.Length == 0 ? [] : feeds.Where(f => FuzzyMatcher.Fold(f.Name).Contains(filter, StringComparison.Ordinal)).ToList();
        var errors = new List<string>();
        var items = await news.LatestAsync(sources.Count > 0 ? sources : feeds, e =>
        {
            lock (errors)
            {
                errors.Add(e);
            }
        }, ct).ConfigureAwait(false);

        var shown = sources.Count > 0 || filter.Length == 0
            ? items
            : items.Where(i => FuzzyMatcher.Fold(i.Title + " " + i.Summary).Contains(filter, StringComparison.Ordinal)).ToList();
        var results = shown.Take(MaxNews).Select((item, i) =>
        {
            var ago = item.Published is { } at ? " · " + RecentFilesPlugin.Ago(at, now) : "";
            return new Result(item.Title, $"{item.Source}{ago}", InfoPlugins.EmojiPrefix + "📰", 1000 - i, _ =>
            {
                open(item.Url);
                return true;
            })
            {
                PreviewText = $"{item.Title}\n\n{item.Summary}\n\n{item.Source}{ago}\n{item.Url}",
                DragData = DragPayload.Url(item.Url),
            };
        }).ToList();

        if (results.Count == 0)
        {
            results.Add(new Result(errors.Count > 0 && items.Count == 0 ? "Haberler alınamadı" : "Eşleşen haber yok",
                errors.Count > 0 ? string.Join(" · ", errors) : "Kaynak adı (bbc, ntv) ya da kelime yaz", null, 1, _ => false));
        }
        else if (errors.Count > 0)
        {
            results.Add(new Result("Bazı kaynaklar okunamadı", string.Join(" · ", errors), null, 0, _ => false));
        }

        return results;
    }

    // ---- Film / dizi ----

    internal static async Task<IReadOnlyList<Result>> MoviesAsync(MovieService movies, string argument, Action<string> open, Action openSettings, CancellationToken ct)
    {
        var google = "https://www.google.com/search?q=" + Uri.EscapeDataString(argument);
        if (!movies.IsConfigured)
        {
            return
            [
                new Result("Film bilgisi için TMDB anahtarı gerekli", "Enter ile Ayarlar → İnternet (ücretsiz, bir kerelik)", null, 2, _ =>
                {
                    openSettings();
                    return true;
                }),
                new Result($"\"{argument}\" için Google'da ara", null, null, 1, _ =>
                {
                    open(google);
                    return true;
                }),
            ];
        }

        var found = await movies.SearchAsync(argument, 3, ct).ConfigureAwait(false);
        var results = new List<Result>();
        foreach (var (movie, i) in found.Take(10).Select((m, i) => (m, i)))
        {
            var heading = movie.Year is { } year ? $"{movie.Title} ({year})" : movie.Title;
            var caption = string.Join(" · ", new[] { movie.KindName, movie.OriginalTitle, movie.Genres.Count > 0 ? string.Join(", ", movie.Genres.Take(3)) : null }
                .Where(s => s is { Length: > 0 }));
            var rating = movie.Rating is { } r ? $"★ {r.ToString("0.0", Turkish)}" : "Puan yok";
            var trailer = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString($"{movie.Title} {movie.Year} fragman");
            var menu = new List<ContextAction>
            {
                new("Fragmanı YouTube'da ara", _ =>
                {
                    open(trailer);
                    return true;
                }),
                new("Google'da ara", _ =>
                {
                    open("https://www.google.com/search?q=" + Uri.EscapeDataString($"{movie.Title} {movie.Year} {movie.KindName.ToLowerInvariant()}"));
                    return true;
                }),
            };
            var preview = $"{heading}\n{caption}\n{rating} ({movie.Votes} oy)\n\n{movie.Overview}";
            if (i < 3)
            {
                var lines = new List<string>
                {
                    movie.Streaming.Count > 0 ? "Türkiye'de: " + string.Join(", ", movie.Streaming) : "Türkiye'de abonelikle izlenebileceği yer bilinmiyor",
                };
                if (movie.RentOrBuy.Count > 0)
                {
                    lines.Add("Kirala / satın al: " + string.Join(", ", movie.RentOrBuy.Take(4)));
                }

                if (movie.Overview.Length > 0)
                {
                    lines.Add(movie.Overview.Length > 170 ? movie.Overview[..170].TrimEnd() + "…" : movie.Overview);
                }

                results.Add(new Result(heading, null, InfoPlugins.EmojiPrefix + (movie.Kind == "tv" ? "📺" : "🎬"), 1000 - i, _ =>
                {
                    open(movie.Url);
                    return true;
                }, menu)
                {
                    Card = new ResultCard(rating, caption, lines, Positive: movie.Rating is { } p ? p >= 7 ? true : p < 5.5 ? false : null : null),
                    PreviewText = preview,
                });
            }
            else
            {
                results.Add(new Result(heading, $"{rating} · {caption}", InfoPlugins.EmojiPrefix + (movie.Kind == "tv" ? "📺" : "🎬"), 900 - i, _ =>
                {
                    open(movie.Url);
                    return true;
                }, menu) { PreviewText = preview });
            }
        }

        return results;
    }

    // ---- Çeviri ----

    internal static async Task<IReadOnlyList<Result>> TranslateAsync(
        Translator translator, string argument, Func<string?> clipboard, IPluginHost? host, Action<string> paste, Action<string> open, CancellationToken ct)
    {
        var (to, text) = SplitLanguage(argument.Trim());
        var fromClipboard = false;
        if (text.Length == 0)
        {
            text = clipboard()?.Trim() ?? "";
            fromClipboard = true;
            if (text.Length == 0)
            {
                return [new Result("Çeviri", "Yaz: /çevir merhaba, /çevir de günaydın; boş bırakırsan panodaki metin çevrilir", null, 1, _ => false)];
            }
        }

        if (text.Length > 4000)
        {
            text = text[..4000];
        }

        var translation = await translator.TranslateAsync(text, to, ct).ConfigureAwait(false);
        if (translation.Text.Length == 0)
        {
            return [];
        }

        var direction = $"{Translator.LanguageName(translation.From)} → {Translator.LanguageName(translation.To)}";
        var title = translation.Text.ReplaceLineEndings(" ");
        var web = $"https://translate.google.com/?sl=auto&tl={translation.To}&text={Uri.EscapeDataString(text)}&op=translate";
        return
        [
            new Result(title.Length > 120 ? title[..120] + "…" : title,
                $"{direction}{(fromClipboard ? " (panodan)" : "")} · Enter: kopyala · Ctrl+Enter: yapıştır", InfoPlugins.EmojiPrefix + "🌐", 1000, ctx =>
                {
                    if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
                    {
                        paste(translation.Text);
                    }
                    else
                    {
                        host?.CopyToClipboard(translation.Text);
                    }

                    return true;
                },
                [
                    new ContextAction("Google Çeviri'de aç", _ =>
                    {
                        open(web);
                        return true;
                    }),
                ])
            {
                PreviewText = $"{translation.Text}\n\n— {direction} —\n{text}",
                DragData = DragPayload.Text(translation.Text),
            },
        ];
    }

    /// <summary>"/çevir de günaydın": baştaki bilinen dil kodu hedef dildir.</summary>
    internal static (string? To, string Text) SplitLanguage(string argument)
    {
        var space = argument.IndexOf(' ');
        var head = space > 0 ? argument[..space] : argument;
        return head.Length == 2 && Translator.Languages.ContainsKey(head)
            ? (head.ToLowerInvariant(), space > 0 ? argument[(space + 1)..].Trim() : "")
            : (null, argument);
    }
}
