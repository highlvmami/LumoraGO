using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class SearchServiceTests
{
    private static (SearchService Service, ListLog Log) Create(TimeSpan? timeout, params (PluginMetadata Meta, IPlugin Plugin)[] plugins)
    {
        var log = new ListLog();
        var manager = new PluginManager(log);
        manager.Initialize(plugins.Select(p => new PluginEntry(p.Meta, p.Plugin)), new NullHost(), Path.GetTempPath());
        return (new SearchService(manager, log, timeout), log);
    }

    [Fact]
    public async Task Merges_results_from_global_plugins_sorted_by_score()
    {
        var (service, _) = Create(null,
            (new PluginMetadata("a", "A"), FakePlugin.Returning(("a-low", 10), ("a-high", 90))),
            (new PluginMetadata("b", "B"), FakePlugin.Returning(("b-mid", 50))));

        var response = await service.SearchAsync("x", CancellationToken.None);

        Assert.Equal(["a-high", "b-mid", "a-low"], response.Results.Select(r => r.Title));
        Assert.Equal("b", response.Results[1].PluginId);
    }

    [Fact]
    public async Task Equal_scores_keep_plugin_order()
    {
        var (service, _) = Create(null,
            (new PluginMetadata("a", "A"), FakePlugin.Returning(("first", 50))),
            (new PluginMetadata("b", "B"), FakePlugin.Returning(("second", 50))));

        var response = await service.SearchAsync("x", CancellationToken.None);

        Assert.Equal(["first", "second"], response.Results.Select(r => r.Title));
    }

    [Fact]
    public async Task Keyword_routes_only_to_its_plugin()
    {
        var global = FakePlugin.Returning(("global", 50));
        var calc = FakePlugin.Returning(("14", 100));
        var (service, _) = Create(null,
            (new PluginMetadata("global", "G"), global),
            (new PluginMetadata("calc", "Hesap", "="), calc));

        var response = await service.SearchAsync("= 2+3*4", CancellationToken.None);

        Assert.Equal("14", Assert.Single(response.Results).Title);
        Assert.Empty(global.Received);
        Assert.Equal("2+3*4", Assert.Single(calc.Received).Search);
    }

    [Fact]
    public async Task Empty_text_queries_nothing()
    {
        var global = FakePlugin.Returning(("global", 50));
        var (service, _) = Create(null, (new PluginMetadata("global", "G"), global));

        var response = await service.SearchAsync("   ", CancellationToken.None);

        Assert.Empty(response.Results);
        Assert.Empty(global.Received);
    }

    [Fact]
    public async Task Throwing_plugin_does_not_break_others()
    {
        var bad = new FakePlugin((_, _) => throw new InvalidOperationException("bozuk"));
        var (service, log) = Create(null,
            (new PluginMetadata("bad", "Bad"), bad),
            (new PluginMetadata("good", "Good"), FakePlugin.Returning(("ok", 10))));

        var response = await service.SearchAsync("x", CancellationToken.None);

        Assert.Equal("ok", Assert.Single(response.Results).Title);
        Assert.Contains(response.Timings, t => t.PluginId == "bad" && t.Outcome == PluginOutcome.Failed);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("bad"));
    }

    [Fact]
    public async Task Slow_plugin_times_out_and_is_cancelled()
    {
        var cancelled = new TaskCompletionSource();
        var slow = new FakePlugin(async (_, ct) =>
        {
            ct.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var (service, _) = Create(TimeSpan.FromMilliseconds(50),
            (new PluginMetadata("slow", "Slow"), slow),
            (new PluginMetadata("fast", "Fast"), FakePlugin.Returning(("fast", 10))));

        var response = await service.SearchAsync("x", CancellationToken.None);

        Assert.Equal("fast", Assert.Single(response.Results).Title);
        Assert.Contains(response.Timings, t => t.PluginId == "slow" && t.Outcome == PluginOutcome.TimedOut);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancelled_search_throws()
    {
        var slow = new FakePlugin(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var (service, _) = Create(TimeSpan.FromSeconds(10), (new PluginMetadata("slow", "Slow"), slow));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("x", cts.Token));
    }

    [Fact]
    public void Plugin_failing_init_is_disabled()
    {
        var log = new ListLog();
        var manager = new PluginManager(log);

        manager.Initialize(
            [
                new PluginEntry(new PluginMetadata("broken", "Broken"), new ThrowingInitPlugin()),
                new PluginEntry(new PluginMetadata("fine", "Fine", "f"), FakePlugin.Returning()),
            ],
            new NullHost(),
            Path.GetTempPath());

        Assert.Equal("fine", Assert.Single(manager.Plugins).Metadata.Id);
        Assert.Contains("f", manager.ActionKeywords);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("broken"));
    }
}
