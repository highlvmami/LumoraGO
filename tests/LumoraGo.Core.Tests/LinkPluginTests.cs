using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class LinkPluginTests
{
    [Theory]
    [InlineData("/git", "https://github.com/highlvmami")]
    [InlineData("/linkedin", "https://www.linkedin.com/feed/")]
    [InlineData("/linkedln", "https://www.linkedin.com/feed/")]
    public async Task Keyword_opens_fixed_page(string text, string expectedUrl)
    {
        var opened = new List<string>();
        var plugins = new[] { LinkPlugin.GitHub(opened.Add), LinkPlugin.LinkedIn(opened.Add), LinkPlugin.LinkedIn(opened.Add, misspelled: true) };
        var manager = new PluginManager();
        manager.Initialize(plugins.Select(p => new PluginEntry(p.Metadata, p)).ToList(), new NullHost(), Path.GetTempPath());

        var response = await new SearchService(manager, pluginTimeout: TestPlugins.Timeout).SearchAsync(text, CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.True(result.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(expectedUrl, Assert.Single(opened));
    }
}
