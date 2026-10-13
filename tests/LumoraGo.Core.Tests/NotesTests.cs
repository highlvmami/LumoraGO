using LumoraGo.Core.Notes;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class NotesTests
{
    [Fact]
    public void Store_orders_open_first_and_survives_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-notes-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotesStore(path);
            var bread = store.Add("ekmek al", new DateTime(2026, 10, 13, 9, 0, 0));
            store.Add("faturayı öde", new DateTime(2026, 10, 13, 10, 0, 0));
            store.Add("  spor  ", new DateTime(2026, 10, 13, 11, 0, 0));
            store.Toggle(bread.Id);

            var reloaded = new NotesStore(path).Notes;
            Assert.Equal(["spor", "faturayı öde", "ekmek al"], reloaded.Select(n => n.Text));
            Assert.True(reloaded[2].Done);

            store.RemoveDone();
            Assert.Equal(2, new NotesStore(path).Notes.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Plugin_adds_filters_and_toggles()
    {
        var store = new NotesStore(null);
        store.Add("Süt al");
        var host = new NullHost();
        var plugin = new NotesPlugin(store);
        plugin.Init(new PluginContext(NotesPlugin.Metadata, "", host));

        var add = await plugin.QueryAsync(new Query("/not fatura öde", "/not", "fatura öde"), CancellationToken.None);
        Assert.Equal("Not ekle: fatura öde", Assert.Single(add).Title);
        Assert.False(add[0].Action(new ActionContext(ActionModifiers.None)));

        var all = await plugin.QueryAsync(new Query("/not", "/not", ""), CancellationToken.None);
        Assert.Equal(["fatura öde", "Süt al"], all.Select(r => r.Title));

        var milk = (await plugin.QueryAsync(new Query("/not sut", "/not", "sut"), CancellationToken.None))[1];
        Assert.Equal("Süt al", milk.Title);
        milk.Action(new ActionContext(ActionModifiers.None));
        Assert.True(store.Notes.Single(n => n.Text == "Süt al").Done);
        lock (host.Refreshes)
        {
            Assert.Contains("/not", host.Refreshes);
        }
    }

    [Fact]
    public async Task Empty_store_shows_hint()
    {
        var plugin = new NotesPlugin(new NotesStore(null));
        var results = await plugin.QueryAsync(new Query("/not", "/not", ""), CancellationToken.None);
        Assert.Equal("Henüz not yok", Assert.Single(results).Title);
    }
}
