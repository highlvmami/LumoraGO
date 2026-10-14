using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class WindowLayoutTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(WindowPlacement.LeftHalf, 0, 0, 960, 1040)]
    [InlineData(WindowPlacement.RightHalf, 960, 0, 960, 1040)]
    [InlineData(WindowPlacement.BottomRight, 960, 520, 960, 520)]
    [InlineData(WindowPlacement.CenterThird, 640, 0, 640, 1040)]
    [InlineData(WindowPlacement.RightTwoThirds, 640, 0, 1280, 1040)]
    [InlineData(WindowPlacement.Maximize, 0, 0, 1920, 1040)]
    public void Target_DividesWorkArea(WindowPlacement placement, int left, int top, int width, int height) =>
        Assert.Equal(new ScreenRect(left, top, width, height), WindowLayoutPlugin.Target(placement, Work, new(10, 10, 800, 600)));

    [Fact]
    public void Target_RespectsSecondScreenOffset() =>
        Assert.Equal(new ScreenRect(2880, 0, 960, 1040),
            WindowLayoutPlugin.Target(WindowPlacement.RightHalf, Work with { Left = 1920 }, default));

    [Fact]
    public void Center_KeepsSize() =>
        Assert.Equal(new ScreenRect(560, 220, 800, 600), WindowLayoutPlugin.Target(WindowPlacement.Center, Work, new(10, 10, 800, 600)));

    [Fact]
    public void MoveTo_ScalesAndClampsIntoOtherScreen()
    {
        var moved = WindowLayoutPlugin.MoveTo(new(960, 0, 960, 1040), Work, new(1920, 0, 1280, 680));
        Assert.Equal(new ScreenRect(2560, 0, 640, 680), moved);
    }

    [Fact]
    public async Task Keyword_ListsAllAndAppliesChoice()
    {
        WindowPlacement? applied = null;
        var plugin = new WindowLayoutPlugin(p => applied = p, keyword: true);
        var all = await plugin.QueryAsync(new Query("/pencere", "/pencere", ""), CancellationToken.None);
        Assert.Equal(WindowLayoutPlugin.All.Count, all.Count);

        var right = await plugin.QueryAsync(new Query("/pencere sağ", "/pencere", "sağ"), CancellationToken.None);
        Assert.Equal("Sağ yarı", right[0].Title);
        right[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(WindowPlacement.RightHalf, applied);
    }

    [Theory]
    [InlineData("sol yarı", "Sol yarı")]
    [InlineData("tam ekran", "Tam ekran")]
    [InlineData("diğer ekr", "Diğer ekrana taşı")]
    public async Task Global_MatchesFullNames(string text, string title)
    {
        var plugin = new WindowLayoutPlugin(_ => { });
        var results = await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None);
        Assert.Contains(results, r => r.Title == title);
    }

    [Theory]
    [InlineData("so")]
    [InlineData("ort")]
    [InlineData("spotify")]
    public async Task Global_IgnoresShortOrUnrelated(string text)
    {
        var plugin = new WindowLayoutPlugin(_ => { });
        Assert.Empty(await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None));
    }
}
