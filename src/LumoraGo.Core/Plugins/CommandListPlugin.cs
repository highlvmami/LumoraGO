using System.Globalization;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <param name="Title">Listede görünen ad.</param>
/// <param name="Subtitle">Ne yaptığı veya nasıl yazıldığı.</param>
/// <param name="Action">Seçilince çalışır. true dönerse pencere gizlenir.</param>
/// <param name="AutoComplete">Tab ile kutuya yazılacak metin (örn. "/version").</param>
public sealed record CommandInfo(string Title, string Subtitle, Func<ActionContext, bool> Action, string? AutoComplete = null);

/// <summary>
/// "/liste" yazılınca yazılabilecek tüm komutları A'dan Z'ye listeler. "/liste çık" gibi devam edilirse listeyi süzer.
/// </summary>
public sealed class CommandListPlugin : IPlugin
{
    public const string Keyword = "/liste";
    public static readonly PluginMetadata Metadata = new("lumorago.commandlist", "Komut listesi", Keyword);

    // Türkçe alfabe sırası: Ç, C'den sonra; Ş, S'den sonra gelir.
    private static readonly StringComparer TurkishOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    private readonly Func<IEnumerable<CommandInfo>> _source;

    /// <param name="source">Her sorguda çağrılır, böylece sonradan eklenen eklentilerin komutları da listede görünür.</param>
    public CommandListPlugin(Func<IEnumerable<CommandInfo>> source)
    {
        _source = source;
    }

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var commands = _source()
            .Where(c => query.IsEmpty || CommandsPlugin.Score(c.Title, query.Search) > 0)
            .OrderBy(c => c.Title, TurkishOrder)
            .ToList();

        // Arama servisi puana göre sıraladığı için alfabetik sırayı azalan puanla koru.
        var results = commands
            .Select((c, i) => new Result(c.Title, c.Subtitle, null, commands.Count - i, c.Action) { AutoComplete = c.AutoComplete })
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
