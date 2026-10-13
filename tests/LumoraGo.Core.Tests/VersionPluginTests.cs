using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class VersionPluginTests
{
    [Fact]
    public async Task Shows_version_status_then_notes_in_order()
    {
        var manager = new PluginManager();
        manager.Initialize(
            [new PluginEntry(VersionPlugin.Metadata, new VersionPlugin(() => new VersionInfo("0.2.0", ["Bir", "İki"], "Güncel")))],
            new NullHost(),
            Path.GetTempPath());
        var service = new SearchService(manager);

        var response = await service.SearchAsync("/v", CancellationToken.None);

        Assert.Equal(["LumoraGo 0.2.0", "Bir", "İki"], response.Results.Select(r => r.Title));
        Assert.Equal("Güncel", response.Results[0].Subtitle);
    }

    [Fact]
    public async Task Says_so_when_there_are_no_notes()
    {
        var plugin = new VersionPlugin(() => new VersionInfo("0.2.0", [], "Güncel"));

        var results = await plugin.QueryAsync(new Query("/v", "/v", ""), CancellationToken.None);

        Assert.Equal("Bu sürüm için sürüm notu yok", results[1].Title);
    }
}
