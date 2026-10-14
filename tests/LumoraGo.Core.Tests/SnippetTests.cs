using LumoraGo.Core.Plugins;
using LumoraGo.Core.Snippets;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class SnippetTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 5, 0);

    private static Snippet New(string name, string keyword, string content, string category = "") =>
        new(Guid.NewGuid(), name, keyword, content, category, Now);

    [Theory]
    [InlineData("adres", ";adres")]
    [InlineData(" ;İban ", ";iban")]
    [InlineData("ev adresi", ";ev-adresi")]
    [InlineData(";", "")]
    public void Keyword_IsNormalized(string input, string expected) => Assert.Equal(expected, SnippetStore.NormalizeKeyword(input));

    [Fact]
    public void Save_RejectsDuplicateKeywordAndEmptyFields()
    {
        var store = new SnippetStore(null);
        store.Save(New("Ev adresi", "adres", "Atatürk Cd. No:1"));
        Assert.Contains("zaten", store.Validate(New("İş adresi", ";ADRES", "x")));
        Assert.NotNull(store.Validate(New("", "x", "y")));
        Assert.NotNull(store.Validate(New("Ad", "x", "")));
        Assert.Throws<InvalidOperationException>(() => store.Save(New("İş adresi", "adres", "x")));
    }

    [Fact]
    public void Save_UpdatesInPlaceAndKeepsUsage()
    {
        var store = new SnippetStore(null);
        var saved = store.Save(New("IBAN", "iban", "TR00"));
        store.MarkUsed(saved.Id, Now);
        store.Save(saved with { Content = "TR11" });
        var only = Assert.Single(store.All);
        Assert.Equal("TR11", only.Content);
        Assert.Equal(1, only.UseCount);
    }

    [Fact]
    public void Store_PersistsAndImports()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snippets-{Guid.NewGuid()}.json");
        try
        {
            var store = new SnippetStore(path);
            store.Save(New("Selam", "selam", "Merhaba,\nİyi günler", "İş"));
            var reloaded = new SnippetStore(path);
            Assert.Equal("Merhaba,\nİyi günler", Assert.Single(reloaded.All).Content);
            Assert.Equal(["İş"], reloaded.Categories);

            var other = new SnippetStore(null);
            other.Save(New("Başka", "selam", "x"));
            Assert.Equal(1, other.Import(store.Export()));
            Assert.Equal(2, other.All.Count);
            Assert.Contains(other.All, s => s.Name == "Selam" && s.Keyword == "");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Expand_FillsPlaceholders()
    {
        var result = SnippetExpander.Expand("{tarih} {saat} {gün} {ay} {yıl} {yarın} [{pano}]", Now, "kopya");
        Assert.Equal("14.10.2026 09:05 Çarşamba Ekim 2026 15.10.2026 [kopya]", result.Text);
        Assert.Equal(0, result.CharsAfterCursor);
    }

    [Fact]
    public void Expand_ReportsCursorPosition()
    {
        var result = SnippetExpander.Expand("Sayın {imleç},\r\nSaygılar", Now, null);
        Assert.Equal("Sayın ,\r\nSaygılar", result.Text);
        Assert.Equal(",\nSaygılar".Length, result.CharsAfterCursor);
    }

    private static (SnippetsPlugin Plugin, SnippetStore Store, List<ExpandedSnippet> Pasted, List<Guid?> Opened) Setup(string? keyword = null)
    {
        var store = new SnippetStore(null);
        store.Save(New("Ev adresi", "adres", "Atatürk Cd. {tarih}", "Kişisel"));
        store.Save(New("IBAN", "iban", "TR00 0000", "Banka"));
        var pasted = new List<ExpandedSnippet>();
        var opened = new List<Guid?>();
        return (new SnippetsPlugin(store, pasted.Add, opened.Add, () => "pano", keyword, () => Now), store, pasted, opened);
    }

    [Fact]
    public async Task Global_RespondsOnlyToSemicolon()
    {
        var (plugin, store, pasted, _) = Setup();
        Assert.Empty(await plugin.QueryAsync(new Query("adres", null, "adres"), CancellationToken.None));

        var results = await plugin.QueryAsync(new Query(";adr", null, ";adr"), CancellationToken.None);
        Assert.Equal("Ev adresi", results[0].Title);
        Assert.Equal(";adres", results[0].AutoComplete);
        results[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal("Atatürk Cd. 14.10.2026", Assert.Single(pasted).Text);
        Assert.Equal(1, store.All.Single(s => s.Name == "Ev adresi").UseCount);
    }

    [Fact]
    public async Task Global_OffersEditorWhenNothingMatches()
    {
        var (plugin, _, _, opened) = Setup();
        var row = Assert.Single(await plugin.QueryAsync(new Query(";yok", null, ";yok"), CancellationToken.None));
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal([null], opened);
    }

    [Fact]
    public async Task Keyword_ListsAllPlusEditor()
    {
        var (plugin, _, _, _) = Setup("/metin");
        var results = await plugin.QueryAsync(new Query("/metin", "/metin", ""), CancellationToken.None);
        Assert.Equal(3, results.Count);
        Assert.Equal("Hazır metinleri düzenle", results[^1].Title);

        var bank = await plugin.QueryAsync(new Query("/metin banka", "/metin", "banka"), CancellationToken.None);
        Assert.Equal("IBAN", bank[0].Title);
    }
}
