using LumoraGo.Core.Browser;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class PackTwoTests
{
    private const string ChromeJson = """
        {
          "roots": {
            "bookmark_bar": {
              "name": "Yer işaretleri çubuğu", "type": "folder",
              "children": [
                { "name": "GitHub", "type": "url", "url": "https://github.com" },
                { "name": "İş", "type": "folder", "children": [
                  { "name": "Jira panosu", "type": "url", "url": "https://jira.example.com/board" }
                ] }
              ]
            },
            "other": { "name": "Diğer", "type": "folder", "children": [
              { "name": "", "type": "url", "url": "https://bos.example.com" }
            ] },
            "sync_transaction_version": "1"
          }
        }
        """;

    [Fact]
    public void Parses_chromium_bookmarks_with_folders()
    {
        var items = BookmarkSource.Parse(ChromeJson, "Chrome");
        Assert.Equal(["GitHub", "Jira panosu", "https://bos.example.com"], items.Select(b => b.Title));
        Assert.Equal("Yer işaretleri çubuğu / İş", items[1].Folder);
    }

    [Fact]
    public async Task Bookmark_source_caches_and_plugin_searches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumorago-bm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var chrome = Path.Combine(root, "chrome");
            var edge = Path.Combine(root, "edge");
            File.WriteAllText(chrome, ChromeJson);
            File.WriteAllText(edge, """{ "roots": { "bar": { "type": "folder", "name": "Bar", "children": [ { "type": "url", "name": "GitHub kopya", "url": "https://GITHUB.com" }, { "type": "url", "name": "Haberler", "url": "https://haber.example.com" } ] } } }""");
            var source = new BookmarkSource(() => [(chrome, "Chrome"), (edge, "Edge"), (Path.Combine(root, "yok"), "Brave")]);

            Assert.Equal(4, source.All().Count);

            var opened = new List<string>();
            var plugin = new BookmarksPlugin(source.All, opened.Add);
            plugin.Init(new PluginContext(BookmarksPlugin.Metadata, "", new NullHost()));
            var jira = await plugin.QueryAsync(new Query("/b jira", "/b", "jira"), CancellationToken.None);
            Assert.Equal("Jira panosu", jira[0].Title);
            jira[0].Action(new ActionContext(ActionModifiers.None));
            Assert.Equal(["https://jira.example.com/board"], opened);

            var byFolder = await plugin.QueryAsync(new Query("/b iş", "/b", "iş"), CancellationToken.None);
            Assert.Contains(byFolder, r => r.Title == "Jira panosu");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("bluetooth", "Bluetooth ve cihazlar")]
    [InlineData("varsayılan", "Varsayılan uygulamalar")]
    [InlineData("duvar", "Kişiselleştirme / arka plan")]
    [InlineData("guncelle", "Windows Update")]
    public async Task Windows_settings_are_found(string text, string expected)
    {
        var opened = new List<string>();
        var plugin = new WindowsSettingsPlugin(opened.Add);
        var results = await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None);
        Assert.Equal(expected, results[0].Title);
        Assert.True(results[0].Score <= WindowsSettingsPlugin.MaxScore);
        results[0].Action(new ActionContext(ActionModifiers.None));
        Assert.StartsWith("ms-settings:", opened.Single());
    }

    [Fact]
    public async Task Windows_settings_ignore_short_text() =>
        Assert.Empty(await new WindowsSettingsPlugin(_ => { }).QueryAsync(new Query("ek", null, "ek"), CancellationToken.None));

    [Fact]
    public async Task Recent_files_are_newest_first_and_filtered()
    {
        var now = DateTime.Now;
        var items = new List<RecentItem>
        {
            new("rapor.docx", @"C:\r.lnk", now.AddHours(-3)),
            new("bütçe.xlsx", @"C:\b.lnk", now.AddMinutes(-5)),
        };
        var opened = new List<string>();
        var plugin = new RecentFilesPlugin(() => items, opened.Add);

        var all = await plugin.QueryAsync(new Query("/son", "/son", ""), CancellationToken.None);
        Assert.Equal(["bütçe.xlsx", "rapor.docx"], all.Select(r => r.Title));
        Assert.StartsWith("5 dk önce", all[0].Subtitle);

        var filtered = Assert.Single(await plugin.QueryAsync(new Query("/son butce", "/son", "butce"), CancellationToken.None));
        filtered.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal([@"C:\b.lnk"], opened);
        Assert.Equal("dün", RecentFilesPlugin.Ago(now.AddHours(-30), now));
    }

    [Fact]
    public async Task World_clock_shows_city_time()
    {
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var host = new NullHost();
        var plugin = new WorldClockPlugin(() => now);
        plugin.Init(new PluginContext(WorldClockPlugin.Metadata, "", host));

        var tokyo = Assert.Single(await plugin.QueryAsync(new Query("/saat japonya", "/saat", "japonya"), CancellationToken.None));
        Assert.Equal("Tokyo: 21:00", tokyo.Title);
        tokyo.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["Tokyo 21:00"], host.Copied);

        var ny = Assert.Single(await plugin.QueryAsync(new Query("/saat new york", "/saat", "new york"), CancellationToken.None));
        Assert.Equal("New York: 07:00", ny.Title);

        Assert.Equal(WorldClockPlugin.Cities.Length, (await plugin.QueryAsync(new Query("/saat", "/saat", ""), CancellationToken.None)).Count);
        Assert.Equal("3 saat ileride", WorldClockPlugin.Difference(TimeSpan.FromHours(3)));
        Assert.Equal("5,5 saat geride", WorldClockPlugin.Difference(TimeSpan.FromHours(-5.5)));
    }
}
