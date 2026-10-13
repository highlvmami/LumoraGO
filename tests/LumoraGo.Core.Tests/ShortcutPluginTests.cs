using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class ShortcutPluginTests
{
    [Fact]
    public async Task Keyword_runs_its_action()
    {
        var ran = new List<string>();
        var manager = new PluginManager();
        manager.Initialize(
            [
                Entry(new ShortcutPlugin("/kilit", "Bilgisayarı kilitle", "", () => ran.Add("kilit"))),
                Entry(new ShortcutPlugin("/uyku", "Uyku moduna al", "", () => ran.Add("uyku"))),
            ],
            new NullHost(),
            Path.GetTempPath());

        var response = await new SearchService(manager, pluginTimeout: TestPlugins.Timeout).SearchAsync("/kilit", CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.Equal("Bilgisayarı kilitle", result.Title);
        Assert.Empty(ran);
        Assert.True(result.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(["kilit"], ran);
    }

    [Fact]
    public async Task Prefix_of_keyword_does_not_run_anything()
    {
        var plugin = new ShortcutPlugin("/kapat", "Bilgisayarı kapat", "", () => throw new InvalidOperationException());
        var manager = new PluginManager();
        manager.Initialize([Entry(plugin)], new NullHost(), Path.GetTempPath());

        var response = await new SearchService(manager, pluginTimeout: TestPlugins.Timeout).SearchAsync("/ka", CancellationToken.None);

        Assert.Empty(response.Results);
    }

    private static PluginEntry Entry(ShortcutPlugin plugin) => new(plugin.Metadata, plugin);
}
