using System.Collections.Concurrent;

namespace LumoraGo.App.Services;

/// <summary>
/// Kabuk (Shell) COM nesneleri STA thread ister. İndeksleme ve simge çıkarma bu tek arka plan thread'inde sırayla yapılır,
/// UI thread'i hiç beklemez.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Post(Action action) => _queue.Add(action);

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
