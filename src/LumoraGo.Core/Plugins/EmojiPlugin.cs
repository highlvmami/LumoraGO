using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/e kalp": emoji arar. Enter önceki pencereye yapıştırır, Ctrl+Enter kopyalar.</summary>
public sealed class EmojiPlugin(Action<string> paste) : IPlugin
{
    public const string Keyword = "/e";
    public static readonly PluginMetadata Metadata = new("lumorago.emoji", "Emoji arama", Keyword);

    /// <summary>Sonuç simgesi olarak emojinin kendisini çizdirmek için <see cref="Result.IconPath"/> öneki.</summary>
    public const string IconPrefix = "emoji:";

    private const int MaxResults = 40;
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var matches = EmojiCatalog.Search(query.Search, MaxResults);
        var results = matches
            .Select((e, i) => new Result($"{e.Emoji}  {e.Name}", "Enter: yapıştır, Ctrl+Enter: kopyala", IconPrefix + e.Emoji, MaxResults - i, ctx =>
            {
                if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
                {
                    _host?.CopyToClipboard(e.Emoji);
                }
                else
                {
                    paste(e.Emoji);
                }

                return true;
            })
            {
                DragData = DragPayload.Text(e.Emoji),
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
