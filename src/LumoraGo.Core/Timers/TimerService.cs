namespace LumoraGo.Core.Timers;

public sealed record RunningTimer(Guid Id, string Label, TimeSpan Duration, DateTime EndsAt);

/// <summary>Bellekte çalışan zamanlayıcılar. Süre dolunca <see cref="Elapsed"/> iş parçacığı havuzundan çağrılır.</summary>
public sealed class TimerService(TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (RunningTimer Info, ITimer Timer)> _timers = [];

    public event Action<RunningTimer>? Elapsed;

    /// <summary>En yakın biteni önce.</summary>
    public IReadOnlyList<RunningTimer> Running
    {
        get
        {
            lock (_gate)
            {
                return _timers.Values.Select(t => t.Info).OrderBy(t => t.EndsAt).ToList();
            }
        }
    }

    public DateTime Now => _time.GetLocalNow().DateTime;

    public RunningTimer Start(TimeSpan duration, string label)
    {
        var info = new RunningTimer(Guid.NewGuid(), label.Trim(), duration, Now + duration);
        lock (_gate)
        {
            var timer = _time.CreateTimer(_ => Fire(info.Id), null, duration, Timeout.InfiniteTimeSpan);
            _timers[info.Id] = (info, timer);
        }

        return info;
    }

    public bool Cancel(Guid id)
    {
        lock (_gate)
        {
            if (!_timers.Remove(id, out var entry))
            {
                return false;
            }

            entry.Timer.Dispose();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _timers.Values)
            {
                entry.Timer.Dispose();
            }

            _timers.Clear();
        }
    }

    private void Fire(Guid id)
    {
        RunningTimer info;
        lock (_gate)
        {
            if (!_timers.Remove(id, out var entry))
            {
                return;
            }

            entry.Timer.Dispose();
            info = entry.Info;
        }

        Elapsed?.Invoke(info);
    }
}
