using LumoraGo.Core.Plugins;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class ColorAndEmojiTests
{
    [Theory]
    [InlineData("#1e90ff", "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)")]
    [InlineData("1E90FF", "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)")]
    [InlineData("#fff", "#FFFFFF", "rgb(255, 255, 255)", "hsl(0, 0%, 100%)")]
    [InlineData("rgb(255, 0, 0)", "#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)")]
    [InlineData("hsl(120, 100%, 25%)", "#008000", "rgb(0, 128, 0)", "hsl(120, 100%, 25%)")]
    [InlineData("rgba(0, 0, 0, 0.5)", "#00000080", "rgba(0, 0, 0, 0.5)", "hsla(0, 0%, 0%, 0.5)")]
    [InlineData("#ff000080", "#FF000080", "rgba(255, 0, 0, 0.5)", "hsla(0, 100%, 50%, 0.5)")]
    public void Parses_and_formats_colors(string input, string hex, string rgb, string hsl)
    {
        Assert.True(ColorCode.TryParse(input, out var c));
        Assert.Equal(hex, ColorCode.ToHex(c));
        Assert.Equal(rgb, ColorCode.ToRgb(c));
        Assert.Equal(hsl, ColorCode.ToHsl(c));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456")]
    [InlineData("face")]
    [InlineData("#12")]
    [InlineData("rgb(300, 0, 0)")]
    [InlineData("hsl(10, 150%, 50%)")]
    [InlineData("chrome")]
    public void Rejects_non_colors(string input) => Assert.False(ColorCode.TryParse(input, out _));

    [Fact]
    public async Task Color_plugin_copies_selected_format()
    {
        var host = new NullHost();
        var plugin = new ColorPlugin();
        plugin.Init(new PluginContext(ColorPlugin.Metadata, "", host));

        var results = await plugin.QueryAsync(new Query("#f00", null, "#f00"), CancellationToken.None);
        Assert.Equal(["#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)"], results.Select(r => r.Title));
        Assert.All(results, r => Assert.Equal("color:#FF0000", r.IconPath));

        results[1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["rgb(255, 0, 0)"], host.Copied);
    }

    [Fact]
    public void Emoji_search_matches_turkish_and_english()
    {
        Assert.Contains(EmojiCatalog.Search("kalp", 10), e => e.Emoji == "❤️");
        Assert.Contains(EmojiCatalog.Search("heart", 10), e => e.Emoji == "❤️");
        Assert.NotEmpty(EmojiCatalog.Search("", 10));
        Assert.Empty(EmojiCatalog.Search("qqqzzz", 10));
    }

    [Fact]
    public async Task Emoji_plugin_pastes_or_copies()
    {
        var pasted = new List<string>();
        var host = new NullHost();
        var plugin = new EmojiPlugin(pasted.Add);
        plugin.Init(new PluginContext(EmojiPlugin.Metadata, "", host));

        var results = await plugin.QueryAsync(new Query("/e kalp", "/e", "kalp"), CancellationToken.None);
        var heart = results.First(r => r.IconPath == "emoji:❤️");

        heart.Action(new ActionContext(ActionModifiers.None));
        heart.Action(new ActionContext(ActionModifiers.Ctrl));
        Assert.Equal(["❤️"], pasted);
        Assert.Equal(["❤️"], host.Copied);
    }
}
