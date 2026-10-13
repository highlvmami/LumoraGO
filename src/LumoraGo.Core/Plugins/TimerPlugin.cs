using LumoraGo.Core.Timers;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/t 10dk çay": zamanlayıcı kurar, süre dolunca bildirim gösterir. "/t" çalışanları listeler, Enter iptal eder.</summary>
public sealed class TimerPlugin(TimerService timers) : IPlugin
{
    public const string Keyword = "/t";
    public static readonly PluginMetadata Metadata = new("lumorago.timer", "Zamanlayıcı", Keyword);

    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var results = new List<Result>();
        var now = timers.Now;
        var text = query.Search.Trim();

        if (TimerRequest.TryParse(text, now, out var duration, out var label))
        {
            var name = label.Length > 0 ? label : "Zamanlayıcı";
            results.Add(new Result(
                $"{TimerRequest.Format(duration)} sonra: {name}",
                $"Enter ile kur · {now + duration:HH:mm}'de bildirim gelir",
                null,
                10_000,
                _ =>
                {
                    timers.Start(duration, label);
                    _host?.ChangeQuery(Keyword + " ");
                    return false;
                }));
        }
        else if (text.Length > 0)
        {
            results.Add(new Result("Süreyi anlamadım", "Örnek: /t 10dk çay, /t 1sa 30dk, /t 90sn, /t 14:30 ilaç", null, 10_000, _ => false));
        }

        var running = timers.Running;
        results.AddRange(running.Select((t, i) => new Result(
            $"{(t.Label.Length > 0 ? t.Label : "Zamanlayıcı")} · {TimerRequest.Format(Remaining(t, now))} kaldı",
            $"{t.EndsAt:HH:mm}'de biter · Enter ile iptal et",
            null,
            running.Count - i,
            _ =>
            {
                timers.Cancel(t.Id);
                _host?.RefreshResults(Keyword);
                return false;
            })));

        if (results.Count == 0)
        {
            results.Add(new Result("Çalışan zamanlayıcı yok", "Örnek: /t 10dk çay, /t 1sa 30dk, /t 14:30 ilaç", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static TimeSpan Remaining(RunningTimer timer, DateTime now)
    {
        var left = timer.EndsAt - now;
        // Kalan süreyi yukarı yuvarla; "0 sn kaldı" görünmesin.
        return left <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
    }
}
