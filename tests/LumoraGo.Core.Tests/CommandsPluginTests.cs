using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class CommandsPluginTests
{
    [Theory]
    [InlineData("LumoraGo'dan çık", "lum", 100)]
    [InlineData("LumoraGo'dan çık", "çık", 80)]
    [InlineData("LumoraGo'dan çık", "CIK", 80)]
    [InlineData("LumoraGo'dan çık", "cik", 80)]
    [InlineData("LumoraGo'dan çık", "dan", 50)]
    [InlineData("LumoraGo'dan çık", "xyz", 0)]
    [InlineData("LumoraGo'dan çık", "", 0)]
    public void Scores_prefix_word_and_substring_matches(string title, string search, int expected)
    {
        Assert.Equal(expected, CommandsPlugin.Score(title, search));
    }

    [Fact]
    public async Task Returns_only_matching_commands()
    {
        var plugin = new CommandsPlugin([
            new LauncherCommand("LumoraGo'dan çık", "", _ => true),
            new LauncherCommand("Günlük dosyalarını aç", "", _ => true),
        ]);

        var results = await plugin.QueryAsync(new Query("çık", null, "çık"), CancellationToken.None);

        Assert.Equal("LumoraGo'dan çık", Assert.Single(results).Title);
    }
}
