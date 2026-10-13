using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class WebSearchPluginTests
{
    [Fact]
    public async Task Youtube_keyword_opens_encoded_search()
    {
        var opened = new List<string>();
        var youtube = WebSearchPlugin.YouTube(opened.Add);
        var manager = new PluginManager();
        manager.Initialize([new PluginEntry(youtube.Metadata, youtube)], new NullHost(), Path.GetTempPath());

        var response = await new SearchService(manager).SearchAsync("/yt barış manço & arkadaşları", CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.Equal("YouTube'da ara: barış manço & arkadaşları", result.Title);
        Assert.True(result.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(
            "https://www.youtube.com/results?search_query=bar%C4%B1%C5%9F%20man%C3%A7o%20%26%20arkada%C5%9Flar%C4%B1",
            Assert.Single(opened));
    }

    [Fact]
    public async Task Google_with_empty_search_opens_homepage()
    {
        var opened = new List<string>();
        var google = WebSearchPlugin.Google(opened.Add);

        var result = Assert.Single(await google.QueryAsync(new Query("/gg", "/gg", ""), CancellationToken.None));
        result.Action(new ActionContext(ActionModifiers.None));

        Assert.Equal("https://www.google.com", Assert.Single(opened));
    }
}
