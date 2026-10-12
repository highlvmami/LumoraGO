using LumoraGo.Core.Apps;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class AppsPluginTests
{
    private sealed class FakeSource(params AppEntry[] apps) : IAppSource
    {
        public IReadOnlyList<AppEntry> Apps { get; } = apps;
    }

    private sealed class FakeLauncher : IAppLauncher
    {
        public List<(AppEntry App, LaunchMode Mode)> Launched { get; } = [];

        public void Launch(AppEntry app, LaunchMode mode) => Launched.Add((app, mode));
    }

    private static AppEntry App(string name, string? target = null) =>
        new(name, name, "", name + ".lnk", target, AppKind.Shortcut);

    private static async Task<IReadOnlyList<Result>> Query(AppsPlugin plugin, string text) =>
        await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None);

    [Fact]
    public async Task Finds_apps_by_fuzzy_name()
    {
        var plugin = new AppsPlugin(
            new FakeSource(App("Visual Studio Code"), App("Visual Studio Installer"), App("Notepad")),
            new FakeLauncher(),
            new UsageStore(null));

        var results = await Query(plugin, "vsc");

        Assert.Equal("Visual Studio Code", results[0].Title);
        Assert.DoesNotContain(results, r => r.Title == "Notepad");
    }

    [Fact]
    public async Task Finds_apps_by_exe_name()
    {
        var plugin = new AppsPlugin(
            new FakeSource(App("Visual Studio Code", @"C:\Code\Code.exe")),
            new FakeLauncher(),
            new UsageStore(null));

        Assert.Single(await Query(plugin, "code"));
    }

    [Fact]
    public async Task Used_app_ranks_higher_next_time()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var usage = new UsageStore(null, now: () => now);
        var plugin = new AppsPlugin(new FakeSource(App("Character Map"), App("Google Chrome")), new FakeLauncher(), usage);

        var before = await Query(plugin, "chr");
        var chrome = before.Single(r => r.Title == "Google Chrome");
        chrome.Action(new ActionContext(ActionModifiers.None));
        var after = await Query(plugin, "chr");

        Assert.Equal("Google Chrome", after[0].Title);
    }

    [Theory]
    [InlineData(ActionModifiers.None, LaunchMode.Normal)]
    [InlineData(ActionModifiers.Ctrl, LaunchMode.OpenFolder)]
    [InlineData(ActionModifiers.Shift, LaunchMode.AsAdmin)]
    public async Task Modifiers_select_launch_mode(ActionModifiers modifiers, LaunchMode expected)
    {
        var launcher = new FakeLauncher();
        var plugin = new AppsPlugin(new FakeSource(App("Notepad")), launcher, new UsageStore(null));

        (await Query(plugin, "note"))[0].Action(new ActionContext(modifiers));

        Assert.Equal(expected, Assert.Single(launcher.Launched).Mode);
    }

    [Fact]
    public void Usage_survives_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-usage-{Guid.NewGuid():N}.json");
        try
        {
            new UsageStore(path).Record("Notepad");
            SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(5));

            Assert.True(new UsageStore(path).Boost("notepad") > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
