using LumoraGo.Core.Clipboard;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/c": pano geçmişi. Enter seçileni önceki pencereye yapıştırır, Ctrl+Enter sadece kopyalar.</summary>
public sealed class ClipboardPlugin : IPlugin
{
    public const string Keyword = "/c";
    public static readonly PluginMetadata Metadata = new("lumorago.clipboard", "Pano geçmişi", Keyword);

    private const int MaxResults = 50;
    private const int TitleLength = 80;

    private readonly ClipboardHistory _history;
    private readonly Action<string> _paste;
    private IPluginHost? _host;

    /// <param name="paste">Metni panoya koyar, launcher'ı gizler ve önceki pencereye yapıştırır.</param>
    public ClipboardPlugin(ClipboardHistory history, Action<string> paste)
    {
        _history = history;
        _paste = paste;
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var entries = _history.Entries;
        if (entries.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>(
                [new Result("Pano geçmişi boş", "Bir şey kopyaladığında burada görünür (sadece bellekte tutulur)", null, 1, _ => false)]);
        }

        var folded = FuzzyMatcher.Fold(query.Search);
        var results = entries
            .Where(e => folded.Length == 0 || FuzzyMatcher.Fold(e.Text).Contains(folded, StringComparison.Ordinal))
            .Take(MaxResults)
            .Select((e, i) => ToResult(e, MaxResults - i))
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static string OneLine(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= TitleLength ? line : line[..TitleLength] + "…";
    }

    private Result ToResult(ClipboardEntry entry, int score)
    {
        var lines = entry.Text.Count(c => c == '\n') + 1;
        var subtitle = $"{entry.CopiedAt:HH:mm}{(lines > 1 ? $" · {lines} satır" : "")} · Enter: yapıştır, Ctrl+Enter: kopyala";
        return new Result(OneLine(entry.Text), subtitle, null, score, ctx => Use(entry.Text, ctx), Menu(entry.Text));
    }

    private bool Use(string text, ActionContext ctx)
    {
        if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
        {
            _host?.CopyToClipboard(text);
            return true;
        }

        _paste(text);
        return true;
    }

    private IReadOnlyList<ContextAction> Menu(string text) =>
    [
        new("Yapıştır", _ =>
        {
            _paste(text);
            return true;
        }),
        new("Kopyala", _ =>
        {
            _host?.CopyToClipboard(text);
            return true;
        }),
        new("Geçmişten sil", _ =>
        {
            _history.Remove(text);
            _host?.ChangeQuery(Keyword + " ");
            return false;
        }),
        new("Tüm geçmişi temizle", _ =>
        {
            _history.Clear();
            _host?.ChangeQuery(Keyword + " ");
            return false;
        }),
    ];
}
