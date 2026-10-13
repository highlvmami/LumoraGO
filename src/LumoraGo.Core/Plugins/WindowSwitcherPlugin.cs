using LumoraGo.Core.Search;
using LumoraGo.Core.Windows;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/w chrome": açık pencereler arasında başlık veya uygulama adıyla arar; Enter pencereyi öne getirir.</summary>
public sealed class WindowSwitcherPlugin(
    Func<IReadOnlyList<OpenWindow>> listWindows,
    Action<OpenWindow> activate,
    Action<OpenWindow> close) : IPlugin
{
    public const string Keyword = "/w";
    public static readonly PluginMetadata Metadata = new("lumorago.windows", "Pencere geçişi", Keyword);

    private const int MaxResults = 50;
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var windows = listWindows();
        var folded = FuzzyMatcher.Fold(query.Search.Trim());

        // Pencereler Windows'un Z sırasıyla gelir (en son kullanılan önce); boş aramada bu sıra korunur.
        var results = windows
            .Select((w, order) => (Window: w, Order: order, Score: Match(folded, w)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Order)
            .Take(MaxResults)
            .Select((x, i) => ToResult(x.Window, MaxResults - i))
            .ToList();

        if (results.Count == 0)
        {
            var title = windows.Count == 0 ? "Açık pencere yok" : "Eşleşen pencere yok";
            return Task.FromResult<IReadOnlyList<Result>>([new Result(title, null, null, 1, _ => false)]);
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static int Match(string folded, OpenWindow window)
    {
        if (folded.Length == 0)
        {
            return 1;
        }

        return Math.Max(FuzzyMatcher.Score(folded, window.Title), FuzzyMatcher.Score(folded, window.ProcessName));
    }

    private Result ToResult(OpenWindow window, int score) =>
        new(window.Title, window.ProcessName, window.ExePath, score, _ =>
        {
            activate(window);
            return true;
        })
        {
            ContextMenu =
            [
                new ContextAction("Öne getir", _ =>
                {
                    activate(window);
                    return true;
                }),
                new ContextAction("Pencereyi kapat", _ =>
                {
                    close(window);
                    _host?.RefreshResults(Keyword);
                    return false;
                }),
            ],
        };
}
