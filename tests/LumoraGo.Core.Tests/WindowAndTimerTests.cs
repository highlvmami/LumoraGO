using LumoraGo.Core.Plugins;
using LumoraGo.Core.Timers;
using LumoraGo.Core.Windows;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class WindowAndTimerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 14, 0, 0);

    [Fact]
    public async Task Window_switcher_filters_by_title_and_process()
    {
        var windows = new List<OpenWindow>
        {
            new(1, "Gelen kutusu - Outlook", "OUTLOOK", null),
            new(2, "LumoraGo - GitHub - Google Chrome", "chrome", @"C:\chrome.exe"),
            new(3, "Hesap Makinesi", "CalculatorApp", null),
        };
        var activated = new List<OpenWindow>();
        var closed = new List<OpenWindow>();
        var plugin = new WindowSwitcherPlugin(() => windows, activated.Add, closed.Add);
        plugin.Init(new PluginContext(WindowSwitcherPlugin.Metadata, "", new NullHost()));

        var all = await plugin.QueryAsync(new Query("/w", "/w", ""), CancellationToken.None);
        Assert.Equal(["Gelen kutusu - Outlook", "LumoraGo - GitHub - Google Chrome", "Hesap Makinesi"], all.Select(r => r.Title));

        var chrome = Assert.Single(await plugin.QueryAsync(new Query("/w chrome", "/w", "chrome"), CancellationToken.None));
        Assert.Equal(@"C:\chrome.exe", chrome.IconPath);
        Assert.True(chrome.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(2, activated.Single().Handle);

        Assert.False(chrome.ContextMenu![1].Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(2, closed.Single().Handle);

        var none = Assert.Single(await plugin.QueryAsync(new Query("/w zzzq", "/w", "zzzq"), CancellationToken.None));
        Assert.Equal("Eşleşen pencere yok", none.Title);
    }

    [Theory]
    [InlineData("10dk çay", 600, "çay")]
    [InlineData("10 dk çay demle", 600, "çay demle")]
    [InlineData("1sa 30dk toplantı", 5400, "toplantı")]
    [InlineData("90sn", 90, "")]
    [InlineData("5 yumurta", 300, "yumurta")]
    [InlineData("2h", 7200, "")]
    [InlineData("1,5 saat", 5400, "")]
    [InlineData("14:30 ilaç", 1800, "ilaç")]
    [InlineData("13:00", 23 * 3600, "")]
    public void Parses_timer_text(string text, int seconds, string label)
    {
        Assert.True(TimerRequest.TryParse(text, Now, out var duration, out var parsedLabel));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
        Assert.Equal(label, parsedLabel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("çay")]
    [InlineData("0dk")]
    [InlineData("48sa")]
    public void Rejects_bad_timer_text(string text) => Assert.False(TimerRequest.TryParse(text, Now, out _, out _));

    [Theory]
    [InlineData(90, "1 dk 30 sn")]
    [InlineData(3900, "1 sa 5 dk")]
    [InlineData(45, "45 sn")]
    public void Formats_durations(int seconds, string expected) =>
        Assert.Equal(expected, TimerRequest.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task Timer_service_fires_and_cancels()
    {
        using var service = new TimerService();
        var fired = new TaskCompletionSource<RunningTimer>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Elapsed += t => fired.TrySetResult(t);

        var cancelled = service.Start(TimeSpan.FromMilliseconds(50), "iptal");
        Assert.True(service.Cancel(cancelled.Id));
        service.Start(TimeSpan.FromMilliseconds(100), "çay");
        Assert.Single(service.Running);

        var done = await fired.Task.WaitAsync(TestPlugins.Timeout);
        Assert.Equal("çay", done.Label);
        Assert.Empty(service.Running);
    }

    [Fact]
    public async Task Timer_plugin_starts_lists_and_cancels()
    {
        using var service = new TimerService();
        var host = new NullHost();
        var plugin = new TimerPlugin(service);
        plugin.Init(new PluginContext(TimerPlugin.Metadata, "", host));

        var empty = Assert.Single(await plugin.QueryAsync(new Query("/t", "/t", ""), CancellationToken.None));
        Assert.Equal("Çalışan zamanlayıcı yok", empty.Title);

        var start = Assert.Single(await plugin.QueryAsync(new Query("/t 10dk çay", "/t", "10dk çay"), CancellationToken.None));
        Assert.Equal("10 dk sonra: çay", start.Title);
        start.Action(new ActionContext(ActionModifiers.None));

        var list = Assert.Single(await plugin.QueryAsync(new Query("/t", "/t", ""), CancellationToken.None));
        Assert.StartsWith("çay · ", list.Title);
        list.Action(new ActionContext(ActionModifiers.None));
        Assert.Empty(service.Running);
    }
}
