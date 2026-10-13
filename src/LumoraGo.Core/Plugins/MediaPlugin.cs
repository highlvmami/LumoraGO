using LumoraGo.Core.Search;
using LumoraGo.Core.Desktop;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/ses": ses düzeyi ve medya tuşları. "/ses 40" sesi %40 yapar; "/ses sonraki" gibi yazınca süzülür.</summary>
public sealed class MediaPlugin(IMediaControl media) : IPlugin
{
    public const string Keyword = "/ses";
    public static readonly PluginMetadata Metadata = new("lumorago.media", "Ses ve medya", Keyword);

    private static readonly (MediaKey Key, string Title, string Words)[] Keys =
    [
        (MediaKey.PlayPause, "Oynat / duraklat", "oynat duraklat durdur play pause"),
        (MediaKey.Next, "Sonraki parça", "sonraki ileri next"),
        (MediaKey.Previous, "Önceki parça", "onceki geri previous prev"),
        (MediaKey.Mute, "Sesi kapat / aç", "sessiz kapat ac mute"),
        (MediaKey.VolumeUp, "Sesi artır", "artir yukari ac up"),
        (MediaKey.VolumeDown, "Sesi azalt", "azalt asagi kis down"),
    ];

    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim().TrimEnd('%').TrimStart('%');
        var results = new List<Result>();

        if (int.TryParse(text, out var level))
        {
            level = Math.Clamp(level, 0, 100);
            results.Add(new Result($"Sesi %{level} yap", "Enter ile uygula", null, 100, _ =>
            {
                if (!media.SetVolume(level))
                {
                    _host?.ShowMessage("Ses", "Ses düzeyi değiştirilemedi.");
                }

                _host?.RefreshResults(Keyword);
                return false;
            }));
            return Task.FromResult<IReadOnlyList<Result>>(results);
        }

        var folded = FuzzyMatcher.Fold(text);
        var current = media.GetVolume();
        foreach (var (key, title, words) in Keys)
        {
            if (folded.Length > 0 && !FuzzyMatcher.Fold(title + " " + words).Contains(folded, StringComparison.Ordinal))
            {
                continue;
            }

            var subtitle = current is { } v ? $"Şu anki ses: %{v} · Enter" : "Enter";
            results.Add(new Result(title, subtitle, null, 50 - results.Count, _ =>
            {
                media.Send(key);
                // Ses tuşlarında launcher açık kalsın, art arda basılabilsin.
                _host?.RefreshResults(Keyword);
                return key is MediaKey.PlayPause or MediaKey.Next or MediaKey.Previous;
            }));
        }

        if (results.Count == 0)
        {
            results.Add(new Result("Anlamadım", "Örnek: /ses 40, /ses sonraki, /ses sessiz", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
