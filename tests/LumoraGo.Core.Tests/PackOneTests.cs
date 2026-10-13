using LumoraGo.Core.Desktop;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class PackOneTests
{
    private static Query Global(string text) => new(text, null, text);

    [Fact]
    public async Task Custom_shortcut_opens_target_and_fills_template()
    {
        var opened = new List<string>();
        var shortcuts = new List<CustomShortcut>
        {
            new("/okul", "Okul portalı", "https://okul.example.com"),
            new("ara", "Ekşi'de ara", "https://eksisozluk.com/?q={0}"),
            new("/bos", "", ""),
        };
        var plugin = new CustomShortcutsPlugin(() => shortcuts, opened.Add);

        var school = Assert.Single(await plugin.QueryAsync(Global("/OKUL"), CancellationToken.None));
        Assert.Equal("Okul portalı", school.Title);
        Assert.Equal(CustomShortcutsPlugin.ExactScore, school.Score);
        school.Action(new ActionContext(ActionModifiers.None));

        var search = Assert.Single(await plugin.QueryAsync(Global("ara kedi & köpek"), CancellationToken.None));
        Assert.Equal("Ekşi'de ara: kedi & köpek", search.Title);
        search.Action(new ActionContext(ActionModifiers.None));

        Assert.Equal(["https://okul.example.com", "https://eksisozluk.com/?q=kedi%20%26%20k%C3%B6pek"], opened);

        var hint = Assert.Single(await plugin.QueryAsync(Global("/ok"), CancellationToken.None));
        Assert.True(hint.Score < CustomShortcutsPlugin.ExactScore);
        Assert.Empty(await plugin.QueryAsync(Global("/okul2"), CancellationToken.None));
        Assert.Empty(await plugin.QueryAsync(Global("/bos"), CancellationToken.None));
    }

    [Fact]
    public void Tools_produce_expected_values()
    {
        Assert.Equal("bWVyaGFiYQ==", ToolPlugin.Base64("merhaba").Single().Value);
        var decoded = ToolPlugin.Base64("bWVyaGFiYQ==");
        Assert.Equal("merhaba", decoded[0].Value);

        Assert.Equal("%C5%9Feker%20%26%20%C3%A7ay", ToolPlugin.UrlCoding("şeker & çay").Single().Value);
        Assert.Equal("şeker & çay", ToolPlugin.UrlCoding("%C5%9Feker%20%26%20%C3%A7ay")[0].Value);

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", ToolPlugin.Hashes("hello")[0].Value);
        Assert.Equal(["10", "8", "3"], ToolPlugin.Counts("bir iki üç").Select(o => o.Value));

        var passwords = ToolPlugin.Passwords("24");
        Assert.Equal(24, passwords[0].Value.Length);
        Assert.All(passwords[1].Value, c => Assert.True(char.IsLetterOrDigit(c)));
        Assert.Equal(20, ToolPlugin.Passwords("").First().Value.Length);
        Assert.Equal(6, ToolPlugin.Passwords("2").First().Value.Length);
    }

    [Fact]
    public async Task Tool_plugin_shows_hint_when_empty_and_copies()
    {
        var host = new NullHost();
        var b64 = ToolPlugin.All().Single(t => t.Metadata.ActionKeyword == "/b64");
        b64.Init(new PluginContext(b64.Metadata, "", host));

        var hint = Assert.Single(await b64.QueryAsync(new Query("/b64", "/b64", ""), CancellationToken.None));
        Assert.False(hint.Action(new ActionContext(ActionModifiers.None)));

        var encoded = Assert.Single(await b64.QueryAsync(new Query("/b64 hi", "/b64", "hi"), CancellationToken.None));
        encoded.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["aGk="], host.Copied);
        Assert.Equal(ToolPlugin.All().Count, ToolPlugin.All().Select(t => t.Metadata.ActionKeyword).Distinct().Count());
    }

    [Fact]
    public async Task Kill_groups_processes_by_name()
    {
        var processes = new List<RunningProcess>
        {
            new(1, "chrome", 300L << 20, @"C:\chrome.exe"),
            new(2, "chrome", 200L << 20, @"C:\chrome.exe"),
            new(3, "notepad", 10L << 20, null),
        };
        IReadOnlyList<RunningProcess>? killed = null;
        var plugin = new ProcessKillPlugin(() => processes, list =>
        {
            killed = list;
            return list.Count;
        });
        plugin.Init(new PluginContext(ProcessKillPlugin.Metadata, "", new NullHost()));

        var chrome = Assert.Single(await plugin.QueryAsync(new Query("/kill chr", "/kill", "chr"), CancellationToken.None));
        Assert.Equal("chrome", chrome.Title);
        Assert.StartsWith("2 işlem · 500 MB", chrome.Subtitle);
        chrome.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal([1, 2], killed!.Select(p => p.Id));

        var all = await plugin.QueryAsync(new Query("/kill", "/kill", ""), CancellationToken.None);
        Assert.Equal(["chrome", "notepad"], all.Select(r => r.Title));
    }

    [Fact]
    public async Task Media_sets_volume_and_filters_keys()
    {
        var media = new FakeMedia();
        var plugin = new MediaPlugin(media);
        plugin.Init(new PluginContext(MediaPlugin.Metadata, "", new NullHost()));

        var set = Assert.Single(await plugin.QueryAsync(new Query("/ses %40", "/ses", "%40"), CancellationToken.None));
        Assert.Equal("Sesi %40 yap", set.Title);
        set.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(40, media.Volume);

        var next = Assert.Single(await plugin.QueryAsync(new Query("/ses sonraki", "/ses", "sonraki"), CancellationToken.None));
        Assert.True(next.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal([MediaKey.Next], media.Sent);

        var all = await plugin.QueryAsync(new Query("/ses", "/ses", ""), CancellationToken.None);
        Assert.Equal(6, all.Count);
        Assert.Contains("%40", all[0].Subtitle);
    }

    private sealed class FakeMedia : IMediaControl
    {
        public int Volume { get; private set; } = 70;

        public List<MediaKey> Sent { get; } = [];

        public int? GetVolume() => Volume;

        public bool SetVolume(int percent)
        {
            Volume = percent;
            return true;
        }

        public void Send(MediaKey key) => Sent.Add(key);
    }
}

public class CustomShortcutSettingsTests
{
    [Fact]
    public void Parses_lines_and_reports_problems()
    {
        var shortcuts = LumoraGo.Core.Settings.CustomShortcutText.Parse(
            "# yorum\n/okul | Okul | https://okul.example.com\r\nmail | https://mail.google.com\n\nbozuk satır\n/okul | tekrar\n",
            out var errors);

        Assert.Equal(
            [new CustomShortcut("/okul", "Okul", "https://okul.example.com"), new CustomShortcut("mail", "", "https://mail.google.com")],
            shortcuts);
        Assert.Equal(2, errors.Count);
        Assert.Contains("5.", errors[0]);

        var text = LumoraGo.Core.Settings.CustomShortcutText.Format(shortcuts);
        Assert.Equal(shortcuts, LumoraGo.Core.Settings.CustomShortcutText.Parse(text, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void Shortcuts_survive_settings_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LumoraGo.Core.Settings.SettingsStore(path);
            store.Save(store.Current with { CustomShortcuts = [new CustomShortcut("/x", "X", "https://x.example/{0}")] });
            Assert.Equal("https://x.example/{0}", new LumoraGo.Core.Settings.SettingsStore(path).Current.CustomShortcuts.Single().Target);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
