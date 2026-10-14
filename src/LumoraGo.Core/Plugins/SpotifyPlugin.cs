using LumoraGo.Core.Desktop;
using LumoraGo.Core.Search;
using LumoraGo.Core.Spotify;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/spotify": şarkı, liste, albüm ve sanatçı arar; Enter çalar. Boşken şu an çalanı, kontrolleri ve listelerini gösterir.
/// Spotify bağlı değilken arama Spotify uygulamasında açılır, kontroller medya tuşlarıyla çalışır.
/// </summary>
public sealed class SpotifyPlugin : IPlugin
{
    private static readonly (MediaKey Key, string Title, string Words)[] Controls =
    [
        (MediaKey.PlayPause, "Oynat / duraklat", "oynat duraklat durdur devam play pause"),
        (MediaKey.Next, "Sonraki şarkı", "sonraki gec atla ileri next skip"),
        (MediaKey.Previous, "Önceki şarkı", "onceki geri previous"),
    ];

    private readonly SpotifyApi _api;
    private readonly IMediaControl _media;
    private readonly Action<string> _open;
    private readonly Action _openSettings;
    private readonly LiveLookupPlugin _live;
    private IPluginHost? _host;

    public SpotifyPlugin(SpotifyApi api, IMediaControl media, Action<string> open, Action openSettings, string keyword = "/spotify")
    {
        _api = api;
        _media = media;
        _open = open;
        _openSettings = openSettings;
        Metadata = new("lumorago.spotify." + keyword.Trim('/'), "Spotify", keyword);
        _live = new LiveLookupPlugin(Metadata, "Örnek: /spotify tarkan", LookupAsync, needsArgument: false,
            debounce: TimeSpan.FromMilliseconds(350), cacheFor: TimeSpan.FromSeconds(20));
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
        _live.Init(context);
    }

    public async Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim();
        var folded = FuzzyMatcher.Fold(text);
        var results = new List<Result>();
        foreach (var (key, title, words) in Controls)
        {
            var match = folded.Length == 0 || FuzzyMatcher.Fold(title + " " + words).Split(' ').Any(w => w.StartsWith(folded, StringComparison.Ordinal));
            if (match)
            {
                results.Add(new Result(title, "Spotify · Enter", InfoPlugins.EmojiPrefix + "🎵", folded.Length == 0 ? 700 - results.Count : 2000 - results.Count, _ =>
                {
                    _media.Send(key);
                    return true;
                }));
            }
        }

        if (!_api.IsConnected)
        {
            if (text.Length > 0)
            {
                results.Insert(0, new Result($"Spotify'da ara: {text}", "Spotify uygulamasında açılır", InfoPlugins.EmojiPrefix + "🔎", 1000, _ =>
                {
                    _open("spotify:search:" + Uri.EscapeDataString(text));
                    return true;
                }));
            }

            results.Add(new Result("Spotify'ı bağla", "Launcher'da arayıp Enter'la çalmak için: Ayarlar → Spotify", null, 1, _ =>
            {
                _openSettings();
                return true;
            }));
            return results;
        }

        var live = await _live.QueryAsync(query, ct);
        // Kontrol kelimesi yazıldıysa ("sonraki") aramanın "yükleniyor"u kontrolü itmesin.
        results.AddRange(live.Where(r => !(results.Count > 0 && folded.Length > 0 && r.Score <= 1)));
        return results;
    }

    private async Task<IReadOnlyList<Result>> LookupAsync(string argument, IPluginHost? host, CancellationToken ct)
    {
        var results = new List<Result>();
        if (argument.Length == 0)
        {
            var nowTask = _api.NowPlayingAsync(ct);
            var playlistsTask = _api.PlaylistsAsync(ct);
            if (await nowTask is { } now)
            {
                var minutes = $"{now.ProgressMs / 60000}:{now.ProgressMs / 1000 % 60:00} / {now.DurationMs / 60000}:{now.DurationMs / 1000 % 60:00}";
                results.Add(new Result($"{(now.IsPlaying ? "▶" : "⏸")} {now.Track}", $"{now.Artist} · {minutes}{(now.Device is null ? "" : " · " + now.Device)}",
                    InfoPlugins.EmojiPrefix + "🎧", 900, _ =>
                    {
                        _media.Send(MediaKey.PlayPause);
                        return true;
                    }));
            }

            results.AddRange((await playlistsTask).Select((p, i) => ToResult(p, 500 - i)));
            return results;
        }

        var found = await _api.SearchAsync(argument, ct);
        results.AddRange(found.Select((item, i) => ToResult(item, 1000 - i)));
        results.Add(new Result($"Spotify'da ara: {argument}", "Spotify uygulamasında açılır", InfoPlugins.EmojiPrefix + "🔎", 10, _ =>
        {
            _open("spotify:search:" + Uri.EscapeDataString(argument));
            return true;
        }));
        return results;
    }

    private Result ToResult(SpotifyItem item, int score)
    {
        var (emoji, kind) = item.Kind switch
        {
            SpotifyKind.Track => ("🎵", "Şarkı"),
            SpotifyKind.Playlist => ("📃", "Liste"),
            SpotifyKind.Album => ("💿", "Albüm"),
            _ => ("🎤", "Sanatçı"),
        };
        var subtitle = item.Kind == SpotifyKind.Artist ? "Sanatçı · Enter çalar" : $"{kind} · {item.Detail} · Enter çalar";
        return new Result(item.Name, subtitle, InfoPlugins.EmojiPrefix + emoji, score, ctx =>
        {
            if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
            {
                _open(item.Uri);
                return true;
            }

            _ = PlayAsync(item);
            return true;
        },
        [
            new ContextAction("Spotify'da aç", _ =>
            {
                _open(item.Uri);
                return true;
            }),
            new ContextAction("Bağlantıyı kopyala", _ =>
            {
                _host?.CopyToClipboard(item.Url ?? item.Uri);
                return true;
            }),
        ])
        {
            DragData = item.Url is { } url ? DragPayload.Text(url) : null,
        };
    }

    /// <summary>Çalmayı dener; Premium ya da açık cihaz yoksa uygulamada açar, kullanıcı oradan çalar.</summary>
    private async Task PlayAsync(SpotifyItem item)
    {
        try
        {
            await _api.PlayAsync(item, CancellationToken.None);
        }
        catch (SpotifyException ex)
        {
            _host?.Log(LogLevel.Warning, $"Spotify çalınamadı: {ex.Message}");
            _open(item.Uri);
        }
    }
}
