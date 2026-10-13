using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class QueryLearningTests
{
    private static Result R(string title, string plugin = "apps") => new Result(title, null, null, 50, _ => true) { PluginId = plugin };

    [Fact]
    public void Chosen_result_gets_a_boost_for_the_same_and_related_text()
    {
        var learning = new QueryLearning(null);
        var chrome = R("Google Chrome");

        learning.Record("ch", chrome);
        learning.Record("ch", chrome);

        var key = QueryLearning.KeyOf(chrome);
        Assert.Equal(120, learning.Boosts("ch")[key]);
        Assert.Equal(60, learning.Boosts("c")[key]);      // "ch"nin başı: yarım
        Assert.Equal(60, learning.Boosts("chr")[key]);    // "ch" ile başlıyor: yarım
        Assert.Empty(learning.Boosts("x"));
        Assert.Empty(learning.Boosts(""));
    }

    [Fact]
    public void Same_title_from_another_plugin_is_a_different_result()
    {
        var learning = new QueryLearning(null);
        learning.Record("not", R("Notlar", "notes"));

        Assert.False(learning.Boosts("not").ContainsKey(QueryLearning.KeyOf(R("Notlar", "apps"))));
    }

    [Fact]
    public void Text_is_folded_so_case_and_turkish_letters_do_not_matter()
    {
        var learning = new QueryLearning(null);
        var result = R("Görev Yöneticisi");
        learning.Record("  GÖREV ", result);

        Assert.Equal(60, learning.Boosts("gorev")[QueryLearning.KeyOf(result)]);
    }

    [Fact]
    public void Old_choices_fade_with_the_half_life()
    {
        var now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var learning = new QueryLearning(null, utcNow: () => now, halfLife: TimeSpan.FromDays(30));
        var result = R("Chrome");
        learning.Record("ch", result);
        learning.Record("ch", result);

        now = now.AddDays(30);
        Assert.Equal(60, learning.Boosts("ch")[QueryLearning.KeyOf(result)]);

        now = now.AddDays(300);
        Assert.Empty(learning.Boosts("ch"));
    }

    [Fact]
    public void Boost_is_capped()
    {
        var learning = new QueryLearning(null);
        var result = R("Chrome");
        for (var i = 0; i < 50; i++)
        {
            learning.Record("ch", result);
        }

        Assert.Equal(QueryLearning.MaxBoost, learning.Boosts("ch")[QueryLearning.KeyOf(result)]);
    }

    [Fact]
    public void Keeps_at_most_max_entries_dropping_the_weakest()
    {
        var learning = new QueryLearning(null);
        var strong = R("güçlü");
        learning.Record("aa", strong);
        learning.Record("aa", strong);
        for (var i = 0; i < QueryLearning.MaxEntries + 10; i++)
        {
            learning.Record($"q{i}", R($"r{i}"));
        }

        Assert.Equal(QueryLearning.MaxEntries, learning.Count);
        Assert.True(learning.Boosts("aa").ContainsKey(QueryLearning.KeyOf(strong)));
    }

    [Fact]
    public async Task Survives_a_restart_and_can_be_cleared()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumorago-learn-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "learned.json");
        try
        {
            var result = R("Chrome");
            new QueryLearning(path).Record("ch", result);
            await Wait.Until(() => File.Exists(path));
            await Wait.Until(() => new QueryLearning(path).Count == 1);

            var reopened = new QueryLearning(path);
            Assert.Equal(60, reopened.Boosts("ch")[QueryLearning.KeyOf(result)]);

            reopened.Clear();
            Assert.Empty(reopened.Boosts("ch"));
            await Wait.Until(() => new QueryLearning(path).Count == 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Corrupt_file_starts_empty_and_logs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumorago-learn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "learned.json");
            File.WriteAllText(path, "{ bozuk");
            var log = new ListLog();

            Assert.Equal(0, new QueryLearning(path, log).Count);
            Assert.Single(log.Entries);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Search_moves_the_learned_result_to_the_top()
    {
        var learning = new QueryLearning(null);
        var manager = new PluginManager(new ListLog());
        manager.Initialize(
            [
                new PluginEntry(new PluginMetadata("apps", "Uygulamalar"), FakePlugin.Returning(("Calculator", 90), ("Google Chrome", 70))),
                new PluginEntry(new PluginMetadata("files", "Dosyalar"), FakePlugin.Returning(("chess.pdf", 80))),
            ],
            new NullHost(), Path.GetTempPath());
        var service = new SearchService(manager, new ListLog(), TestPlugins.Timeout, learning);

        var before = await service.SearchAsync("ch", CancellationToken.None);
        Assert.Equal(["Calculator", "chess.pdf", "Google Chrome"], before.Results.Select(r => r.Title));

        service.Learn("ch", before.Results[2]);
        var after = await service.SearchAsync("ch", CancellationToken.None);

        Assert.Equal(["Google Chrome", "Calculator", "chess.pdf"], after.Results.Select(r => r.Title));
        Assert.Equal(130, after.Results[0].Score);
    }
}
