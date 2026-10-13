using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/li" yazılınca "/liste" gibi tetik kelimelerini önerir; Tab veya Enter kelimeyi tamamlar.</summary>
public sealed class KeywordHintsPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.keywordhints", "Komut önerileri");

    /// <summary>Uygulama sonuçlarının üstünde, hesap sonucunun altında.</summary>
    public const int HintScore = 900;

    private readonly Func<IEnumerable<PluginMetadata>> _plugins;
    private IPluginHost? _host;

    /// <param name="plugins">Her sorguda okunur; tetik kelimesi olanlar öneri olur.</param>
    public KeywordHintsPlugin(Func<IEnumerable<PluginMetadata>> plugins)
    {
        _plugins = plugins;
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var typed = query.RawText.TrimStart();
        if (!typed.StartsWith('/') || typed.Contains(' '))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var results = _plugins()
            .Where(p => p.ActionKeyword is { } k && k.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && k.Length > typed.Length)
            .OrderBy(p => p.ActionKeyword, StringComparer.OrdinalIgnoreCase)
            .Select((p, i) => new Result(p.ActionKeyword!, p.Name, null, HintScore - i, _ =>
            {
                _host?.ChangeQuery(p.ActionKeyword!);
                return false;
            })
            {
                AutoComplete = p.ActionKeyword,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
