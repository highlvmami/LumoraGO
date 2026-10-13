using LumoraGo.Core.Settings;

namespace LumoraGo.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lumorago-settings-{Guid.NewGuid():N}", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path)))
        {
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        }
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        Assert.Equal("Alt+Space", new SettingsStore(_path).Current.Hotkey);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ bozuk");
        Assert.Equal(ThemeChoice.Dark, new SettingsStore(_path).Current.Theme);
    }

    [Fact]
    public void Saved_settings_survive_restart_and_notify()
    {
        var store = new SettingsStore(_path);
        LauncherSettings? notified = null;
        store.Changed += (_, s) => notified = s;

        var settings = store.Current with
        {
            Hotkey = "Ctrl+Shift+K",
            Theme = ThemeChoice.System,
            ClipboardHistory = false,
            DeepSearchFolders = [@"D:\Projeler"],
            DeepSearchSkipNames = ["yedek"],
            FileSearchFolders = [@"E:\Arşiv"],
        };
        store.Save(settings);

        var reloaded = new SettingsStore(_path).Current;
        Assert.Same(settings, notified);
        Assert.Equal("Ctrl+Shift+K", reloaded.Hotkey);
        Assert.Equal(ThemeChoice.System, reloaded.Theme);
        Assert.False(reloaded.ClipboardHistory);
        Assert.Equal([@"D:\Projeler"], reloaded.DeepSearchFolders);
        Assert.Equal(["yedek"], reloaded.DeepSearchSkipNames);
        Assert.Equal([@"E:\Arşiv"], reloaded.FileSearchFolders);
    }

    [Theory]
    [InlineData("Alt+Space", HotkeyModifiers.Alt, "Space")]
    [InlineData("ctrl + shift + K", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "K")]
    [InlineData("Win+F1", HotkeyModifiers.Win, "F1")]
    public void Hotkey_text_parses(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyText.TryParse(text, out var m, out var k));
        Assert.Equal(modifiers, m);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Alt")]
    [InlineData("Alt+A+B")]
    public void Hotkey_text_rejects_incomplete(string text) =>
        Assert.False(HotkeyText.TryParse(text, out _, out _));

    [Fact]
    public void Hotkey_text_formats_in_fixed_order() =>
        Assert.Equal("Ctrl+Alt+Shift+Space", HotkeyText.Format(HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, "Space"));
}
