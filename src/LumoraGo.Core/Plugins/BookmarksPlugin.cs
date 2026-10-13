using LumoraGo.Core.Browser;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/b github": tarayıcı yer imlerinde ad, adres ve klasöre göre arar; Enter açar.</summary>
public sealed class BookmarksPlugin(Func<IReadOnlyList<Bookmark>> bookmarks, Action<string> open) : IPlugin
{
    public const string Keyword = "/b";
    public static readonly PluginMetadata Metadata = new("lumorago.bookmarks", "Yer imleri", Keyword);

    private const int MaxResults = 40;
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var all = bookmarks();
        if (all.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>(
                [new Result("Yer imi bulunamadı", "Chrome, Edge, Brave, Vivaldi ve Opera yer imleri okunur", null, 1, _ => false)]);
        }

        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        var results = all
            .Select((b, i) => (Bookmark: b, Order: i, Score: Match(folded, b)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Order)
            .Take(MaxResults)
            .Select((x, i) => ToResult(x.Bookmark, MaxResults - i))
            .ToList();

        if (results.Count == 0)
        {
            results.Add(new Result("Eşleşen yer imi yok", null, null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static int Match(string folded, Bookmark bookmark)
    {
        if (folded.Length == 0)
        {
            return 1;
        }

        var title = FuzzyMatcher.Score(folded, bookmark.Title);
        var url = FuzzyMatcher.Fold(bookmark.Url).Contains(folded, StringComparison.Ordinal) ? 1 : 0;
        var folder = FuzzyMatcher.Fold(bookmark.Folder).Contains(folded, StringComparison.Ordinal) ? 1 : 0;
        return title > 0 ? title + 10 : url + folder;
    }

    private Result ToResult(Bookmark bookmark, int score)
    {
        var where = bookmark.Folder.Length > 0 ? $"{bookmark.Browser} · {bookmark.Folder}" : bookmark.Browser;
        return new Result(bookmark.Title, $"{where} · {bookmark.Url}", null, score, _ =>
        {
            open(bookmark.Url);
            return true;
        })
        {
            ContextMenu =
            [
                new ContextAction("Aç", _ =>
                {
                    open(bookmark.Url);
                    return true;
                }),
                new ContextAction("Adresi kopyala", _ =>
                {
                    _host?.CopyToClipboard(bookmark.Url);
                    return true;
                }),
            ],
        };
    }
}
