using LumoraGo.Core.Search;
using LumoraGo.Core.Desktop;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/kill chrome": çalışan uygulamayı adıyla bulur, Enter aynı addaki tüm işlemleri sonlandırır.</summary>
public sealed class ProcessKillPlugin(
    Func<IReadOnlyList<RunningProcess>> listProcesses,
    Func<IReadOnlyList<RunningProcess>, int> kill) : IPlugin
{
    public const string Keyword = "/kill";
    public static readonly PluginMetadata Metadata = new("lumorago.kill", "Uygulamayı zorla kapat", Keyword);

    private const int MaxResults = 30;
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        var groups = listProcesses()
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Processes: g.ToList(), Memory: g.Sum(p => p.MemoryBytes)))
            .Select(g => (g.Name, g.Processes, g.Memory, Score: folded.Length == 0 ? 1 : FuzzyMatcher.Score(folded, g.Name)))
            .Where(g => g.Score > 0)
            .OrderByDescending(g => g.Score)
            .ThenByDescending(g => g.Memory)
            .Take(MaxResults)
            .ToList();

        if (groups.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([new Result("Eşleşen işlem yok", "Örnek: /kill chrome", null, 1, _ => false)]);
        }

        var results = groups.Select((g, i) =>
        {
            var count = g.Processes.Count;
            var subtitle = $"{(count > 1 ? $"{count} işlem · " : "")}{FormatBytes(g.Memory)} · Enter ile zorla kapat (kaydedilmemiş işler kaybolur)";
            return new Result(g.Name, subtitle, g.Processes.Select(p => p.ExePath).FirstOrDefault(p => p is not null), MaxResults - i, _ =>
            {
                var killed = kill(g.Processes);
                _host?.ShowMessage("Uygulama kapatıldı", $"{g.Name}: {killed}/{count} işlem sonlandırıldı.");
                return true;
            });
        }).ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
