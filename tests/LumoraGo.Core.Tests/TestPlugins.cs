using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

internal sealed class FakePlugin(Func<Query, CancellationToken, Task<IReadOnlyList<Result>>> query) : IPlugin
{
    public List<Query> Received { get; } = [];

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query q, CancellationToken ct)
    {
        lock (Received)
        {
            Received.Add(q);
        }

        return query(q, ct);
    }

    public static FakePlugin Returning(params (string Title, int Score)[] items) =>
        new((_, _) => Task.FromResult<IReadOnlyList<Result>>(
            items.Select(i => new Result(i.Title, null, null, i.Score, _ => true)).ToList()));
}

internal sealed class ThrowingInitPlugin : IPlugin
{
    public void Init(PluginContext context) => throw new InvalidOperationException("init patladı");

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Result>>([]);
}

internal sealed class NullHost : IPluginHost
{
    public void ChangeQuery(string text)
    {
    }

    public void HideLauncher()
    {
    }

    public void ShowMessage(string title, string message)
    {
    }

    public void Log(LogLevel level, string message)
    {
    }
}

internal sealed class ListLog : ILog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public void Write(LogLevel level, string message)
    {
        lock (Entries)
        {
            Entries.Add((level, message));
        }
    }
}
