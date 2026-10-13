using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

internal static class TestPlugins
{
    /// <summary>CI'da iş parçacığı havuzu ısınırken 200 ms yetmeyebiliyor; zaman aşımını sınamayan testler bunu kullanır.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
}

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

    public void RefreshResults(string keyword)
    {
        lock (Refreshes)
        {
            Refreshes.Add(keyword);
        }
    }

    public List<string> Refreshes { get; } = [];

    public void ShowMessage(string title, string message)
    {
        lock (Messages)
        {
            Messages.Add((title, message));
        }
    }

    public List<(string Title, string Message)> Messages { get; } = [];

    public void Log(LogLevel level, string message)
    {
    }

    public void CopyToClipboard(string text) => Copied.Add(text);

    public List<string> Copied { get; } = [];
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
