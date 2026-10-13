using LumoraGo.Core.Plugins;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class CompletionTests
{
    private static Result R(string title, string? autoComplete = null) =>
        new(title, null, null, 1, _ => true) { AutoComplete = autoComplete };

    [Fact]
    public void Tab_completes_to_title_by_default()
    {
        Assert.Equal("Visual Studio Code", Completion.Complete("vsc", R("Visual Studio Code")));
    }

    [Fact]
    public void Tab_prefers_auto_complete_text()
    {
        Assert.Equal("/liste", Completion.Complete("/li", R("/liste", "/liste")));
        Assert.Equal("14", Completion.Complete("2+3*4", R("= 14", "14")));
    }

    [Fact]
    public void Nothing_to_complete_without_selection_or_when_equal()
    {
        Assert.Null(Completion.Complete("abc", null));
        Assert.Null(Completion.Complete("Notepad", R("Notepad")));
    }

    [Theory]
    [InlineData("vis", "Visual Studio Code", "ual Studio Code")]
    [InlineData("VIS", "Visual Studio Code", "ual Studio Code")]
    [InlineData("vsc", "Visual Studio Code", "")]
    [InlineData("", "Visual Studio Code", "")]
    [InlineData("Notepad", "Notepad", "")]
    public void Ghost_shows_rest_only_for_prefix(string query, string title, string expected)
    {
        Assert.Equal(expected, Completion.GhostSuffix(query, R(title)));
    }

    [Fact]
    public async Task Keyword_hints_suggest_matching_slash_commands()
    {
        var plugin = new KeywordHintsPlugin(() =>
        [
            new PluginMetadata("a", "YouTube'da ara", "/yt"),
            new PluginMetadata("b", "Komut listesi", "/liste"),
            new PluginMetadata("c", "Uygulamalar"),
        ]);

        var results = await plugin.QueryAsync(new Query("/li", null, "/li"), CancellationToken.None);

        var hint = Assert.Single(results);
        Assert.Equal("/liste", hint.Title);
        Assert.Equal("/liste", Completion.Complete("/li", hint));
        Assert.Empty(await plugin.QueryAsync(new Query("v", null, "v"), CancellationToken.None));
        Assert.Empty(await plugin.QueryAsync(new Query("/liste", null, "/liste"), CancellationToken.None));
    }
}
