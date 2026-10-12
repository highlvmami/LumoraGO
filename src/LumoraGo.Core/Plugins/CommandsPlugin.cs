using System.Globalization;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

public sealed record LauncherCommand(string Title, string Subtitle, Func<ActionContext, bool> Action);

/// <summary>
/// LumoraGo'nun kendi komutları ("LumoraGo'dan çık" gibi). Faz 1'de sonuç listesini gerçek veriyle denemek için;
/// asıl bulanık arama Faz 2'de gelir, burada basit önek/içerir eşleşmesi var.
/// </summary>
public sealed class CommandsPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.commands", "LumoraGo komutları");

    private const CompareOptions MatchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private readonly IReadOnlyList<LauncherCommand> _commands;

    public CommandsPlugin(IReadOnlyList<LauncherCommand> commands)
    {
        _commands = commands;
    }

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var results = new List<Result>();
        foreach (var command in _commands)
        {
            var score = Score(command.Title, query.Search);
            if (score > 0)
            {
                results.Add(new Result(command.Title, command.Subtitle, null, score, command.Action));
            }
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static int Score(string title, string search)
    {
        if (search.Length == 0)
        {
            return 0;
        }

        title = FoldDotlessI(title);
        search = FoldDotlessI(search);

        if (Compare.IsPrefix(title, search, MatchOptions))
        {
            return 100;
        }

        // Herhangi bir kelimenin başında eşleşme
        foreach (var word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Compare.IsPrefix(word, search, MatchOptions))
            {
                return 80;
            }
        }

        return Compare.IndexOf(title, search, MatchOptions) >= 0 ? 50 : 0;
    }

    // "ı" ve "İ" aksan değil ayrı harf sayıldığından IgnoreNonSpace onları "i"ye indirmez; "cik" yazınca "çık" bulunsun diye elle katlanır.
    private static string FoldDotlessI(string text) => text.Replace('ı', 'i').Replace('İ', 'I');
}
