using LumoraGo.Core.Search;
using LumoraGo.Core.Snippets;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// Hazır metinler: ";adres" yazıp Enter önceki pencereye yapıştırır. "/metin" hepsini listeler ve düzenleme ekranını açar.
/// Yer tutucular (tarih, saat, pano…) yapıştırırken doldurulur.
/// </summary>
public sealed class SnippetsPlugin : IPlugin
{
    public const string Keyword = "/metin";
    public static readonly PluginMetadata GlobalMetadata = new("lumorago.snippets", "Hazır metinler");

    private readonly SnippetStore _store;
    private readonly Action<ExpandedSnippet> _paste;
    private readonly Action<Guid?> _openEditor;
    private readonly Func<string?> _clipboard;
    private readonly Func<DateTime> _now;
    private readonly string? _keyword;
    private IPluginHost? _host;

    /// <param name="openEditor">Düzenleme ekranını açar; kimlik verilirse o metin seçili gelir.</param>
    /// <param name="keyword">null ise ";" ile başlayan genel aramalara yanıt verir.</param>
    public SnippetsPlugin(SnippetStore store, Action<ExpandedSnippet> paste, Action<Guid?> openEditor, Func<string?> clipboard,
        string? keyword = null, Func<DateTime>? now = null)
    {
        _store = store;
        _paste = paste;
        _openEditor = openEditor;
        _clipboard = clipboard;
        _keyword = keyword;
        _now = now ?? (() => DateTime.Now);
        Metadata = keyword is null ? GlobalMetadata : new("lumorago.snippets." + keyword.Trim('/'), "Hazır metinler", keyword);
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        string search;
        if (_keyword is null)
        {
            var text = query.Search.TrimStart();
            if (!text.StartsWith(SnippetStore.Prefix, StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<Result>>([]);
            }

            search = text[SnippetStore.Prefix.Length..].Trim();
        }
        else
        {
            search = query.Search.Trim().TrimStart(';');
        }

        var folded = FuzzyMatcher.Fold(search);
        var results = new List<Result>();
        foreach (var snippet in _store.All)
        {
            var score = Score(snippet, folded);
            if (score > 0)
            {
                results.Add(ToResult(snippet, score));
            }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (_keyword is not null || results.Count == 0)
        {
            var title = _store.All.Count == 0 ? "İlk hazır metnini oluştur" : "Hazır metinleri düzenle";
            results.Add(new Result(title, "Ad, kısa ad (;adres), kategori, içerik ve yer tutucular", null, 1, _ =>
            {
                _openEditor(null);
                return true;
            }));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static int Score(Snippet snippet, string folded)
    {
        if (folded.Length == 0)
        {
            return 500 + Math.Min(snippet.UseCount, 400);
        }

        var keyword = FuzzyMatcher.Fold(snippet.Keyword.TrimStart(';'));
        if (keyword == folded)
        {
            return 2000;
        }

        if (keyword.StartsWith(folded, StringComparison.Ordinal))
        {
            return 1500 + Math.Min(snippet.UseCount, 400);
        }

        var name = FuzzyMatcher.Score(folded, snippet.Name);
        if (name > 0)
        {
            return 600 + name;
        }

        var category = FuzzyMatcher.Score(folded, snippet.Category);
        if (category > 0)
        {
            return 300 + category;
        }

        return FuzzyMatcher.Fold(snippet.Content).Contains(folded, StringComparison.Ordinal) ? 200 : 0;
    }

    private Result ToResult(Snippet snippet, int score)
    {
        var preview = SnippetExpander.Expand(snippet.Content, _now(), "{pano}").Text;
        var oneLine = preview.ReplaceLineEndings(" ⏎ ");
        var subtitle = (snippet.Keyword.Length > 0 ? snippet.Keyword + " · " : "") + (oneLine.Length > 90 ? oneLine[..90] + "…" : oneLine);
        return new Result(snippet.Name, subtitle, null, score, ctx =>
        {
            var expanded = SnippetExpander.Expand(snippet.Content, _now(), _clipboard());
            _store.MarkUsed(snippet.Id);
            if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
            {
                _host?.CopyToClipboard(expanded.Text);
            }
            else
            {
                _paste(expanded);
            }

            return true;
        },
        [
            new ContextAction("Kopyala", _ =>
            {
                _host?.CopyToClipboard(SnippetExpander.Expand(snippet.Content, _now(), _clipboard()).Text);
                _store.MarkUsed(snippet.Id);
                return true;
            }),
            new ContextAction("Düzenle", _ =>
            {
                _openEditor(snippet.Id);
                return true;
            }),
        ])
        {
            PreviewText = preview,
            AutoComplete = snippet.Keyword.Length > 0 && _keyword is null ? snippet.Keyword : null,
            DragData = DragPayload.Text(preview),
        };
    }
}
