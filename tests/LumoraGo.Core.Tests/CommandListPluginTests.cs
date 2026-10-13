using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class CommandListPluginTests
{
    private static readonly CommandInfo[] Commands =
    [
        new("Şifre üret", "", _ => true),
        new("Çıkış", "", _ => true),
        new("Ayarlar", "", _ => true),
        new("Sistemi kilitle", "", _ => true),
        new("Cevir", "", _ => true),
    ];

    [Fact]
    public async Task Lists_all_commands_in_turkish_alphabetical_order()
    {
        var plugin = new CommandListPlugin(() => Commands);

        var results = await plugin.QueryAsync(new Query("/liste", "/liste", ""), CancellationToken.None);

        Assert.Equal(["Ayarlar", "Cevir", "Çıkış", "Sistemi kilitle", "Şifre üret"], results.Select(r => r.Title));
    }

    [Fact]
    public async Task Search_after_keyword_filters_the_list()
    {
        var plugin = new CommandListPlugin(() => Commands);

        var results = await plugin.QueryAsync(new Query("/liste kil", "/liste", "kil"), CancellationToken.None);

        Assert.Equal("Sistemi kilitle", Assert.Single(results).Title);
    }

    [Fact]
    public async Task Alphabetical_order_survives_search_service_sorting()
    {
        var manager = new PluginManager();
        manager.Initialize(
            [new PluginEntry(CommandListPlugin.Metadata, new CommandListPlugin(() => Commands))],
            new NullHost(),
            Path.GetTempPath());
        var service = new SearchService(manager, pluginTimeout: TestPlugins.Timeout);

        var response = await service.SearchAsync("/liste", CancellationToken.None);

        Assert.Equal(["Ayarlar", "Cevir", "Çıkış", "Sistemi kilitle", "Şifre üret"], response.Results.Select(r => r.Title));
    }
}
