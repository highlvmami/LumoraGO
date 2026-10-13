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
