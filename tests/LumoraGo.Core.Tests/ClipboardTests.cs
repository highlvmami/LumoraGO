using LumoraGo.Core.Clipboard;
using LumoraGo.Core.Plugins;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class ClipboardTests
{
    [Fact]
    public void History_keeps_newest_first_moves_repeats_and_caps()
    {
        var history = new ClipboardHistory(capacity: 3);
        history.Add("bir");
        history.Add("iki");
        history.Add("   ");
        history.Add("bir");
        history.Add("üç");
        history.Add("dört");

        Assert.Equal(["dört", "üç", "bir"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Plugin_filters_pastes_and_copies()
    {
        var history = new ClipboardHistory();
        history.Add("merhaba dünya");
        history.Add("Şifre: 1234\nikinci satır");
        var pasted = new List<string>();
        var host = new NullHost();
        var plugin = new ClipboardPlugin(history, pasted.Add);
        plugin.Init(new PluginContext(ClipboardPlugin.Metadata, "", host));

        var all = await plugin.QueryAsync(new Query("/c", "/c", ""), CancellationToken.None);
        Assert.Equal(["Şifre: 1234 ikinci satır", "merhaba dünya"], all.Select(r => r.Title));
        Assert.Contains("2 satır", all[0].Subtitle);

        var filtered = await plugin.QueryAsync(new Query("/c sifre", "/c", "sifre"), CancellationToken.None);
        var result = Assert.Single(filtered);
        result.Action(new ActionContext(ActionModifiers.None));
        result.Action(new ActionContext(ActionModifiers.Ctrl));

        Assert.Equal(["Şifre: 1234\nikinci satır"], pasted);
        Assert.Equal(["Şifre: 1234\nikinci satır"], host.Copied);
    }

    [Fact]
    public async Task Menu_removes_entry()
    {
        var history = new ClipboardHistory();
        history.Add("sil beni");
        var plugin = new ClipboardPlugin(history, _ => { });
        plugin.Init(new PluginContext(ClipboardPlugin.Metadata, "", new NullHost()));

        var result = (await plugin.QueryAsync(new Query("/c", "/c", ""), CancellationToken.None))[0];
        result.ContextMenu!.Single(a => a.Title == "Geçmişten sil").Action(new ActionContext(ActionModifiers.None));

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Action_menu_turns_context_actions_into_rows_in_order()
    {
        var ran = new List<string>();
        var parent = new Result("Notepad", null, "np.lnk", 5, _ => true,
        [
            new ContextAction("Aç", _ => { ran.Add("aç"); return true; }),
            new ContextAction("Yolu kopyala", _ => { ran.Add("kopyala"); return true; }),
        ]) { PluginId = "lumorago.apps" };

        var rows = ActionMenu.ToResults(parent);

        Assert.Equal(["Aç", "Yolu kopyala"], rows.Select(r => r.Title));
        Assert.True(rows[0].Score > rows[1].Score);
        Assert.All(rows, r => Assert.Equal("Notepad", r.Subtitle));
        rows[1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["kopyala"], ran);
        Assert.False(ActionMenu.HasActions(new Result("x", null, null, 0, _ => true)));
    }
}

public class ClipboardPersistenceTests
{
    [Fact]
    public void Saves_on_dispose_and_restores_after_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-clip-{Guid.NewGuid():N}.json");
        try
        {
            var history = new ClipboardHistory();
            using (new ClipboardPersistence(history, path, saveDelay: TimeSpan.FromHours(1)))
            {
                history.Add("ilk", new DateTime(2026, 10, 13, 9, 0, 0));
                history.Add("ikinci", new DateTime(2026, 10, 13, 10, 0, 0));
            }

            var restored = new ClipboardHistory();
            restored.Add("yeni");
            using (new ClipboardPersistence(restored, path))
            {
                Assert.Equal(["yeni", "ikinci", "ilk"], restored.Entries.Select(e => e.Text));
                Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), restored.Entries[1].CopiedAt);
            }

            ClipboardPersistence.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Saves_after_delay_and_survives_corrupt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-clip-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{bozuk");
            var history = new ClipboardHistory();
            using var persistence = new ClipboardPersistence(history, path, saveDelay: TimeSpan.FromMilliseconds(20));
            Assert.Empty(history.Entries);

            history.Add("merhaba");
            var deadline = DateTime.UtcNow + TestPlugins.Timeout;
            while (!File.ReadAllText(path).Contains("merhaba") && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Contains("merhaba", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
