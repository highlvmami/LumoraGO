using LumoraGo.Core.Contacts;
using LumoraGo.Core.Files;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class DragPayloadTests
{
    private static Result Row() => new("x", null, null, 1, _ => true);

    [Fact]
    public void Explicit_data_wins_then_preview_text_then_nothing()
    {
        Assert.Equal(DragPayload.Url("https://a.com"), DragPayload.For(Row() with { DragData = DragPayload.Url("https://a.com"), PreviewText = "t" }));
        Assert.Equal(DragPayload.Text("uzun metin"), DragPayload.For(Row() with { PreviewText = "uzun metin" }));
        Assert.Null(DragPayload.For(Row() with { PreviewPath = @"C:\uygulama.exe" }));
        Assert.Null(DragPayload.For(Row()));
    }

    [Fact]
    public void File_results_drag_the_real_file()
    {
        var row = new FileResults(_ => { }, _ => { }).Create(@"C:\Belgeler\rapor.pdf", false, "rapor.pdf", "", 10);
        Assert.Equal(DragPayload.File(@"C:\Belgeler\rapor.pdf"), row.DragData);
    }

    [Fact]
    public async Task Calculator_drags_the_plain_number()
    {
        var row = Assert.Single(await new CalculatorPlugin().QueryAsync(new Query("1200*2", null, "1200*2"), CancellationToken.None));
        Assert.Equal(DragKind.Text, row.DragData!.Kind);
        Assert.DoesNotContain(".", row.DragData.Value);
        Assert.Contains("2400", row.DragData.Value);
    }

    [Fact]
    public async Task Contact_drags_the_number()
    {
        var book = new ContactBook(Path.Combine(Path.GetTempPath(), "lumorago-drag-" + Guid.NewGuid().ToString("N"), "c.dat"), b => b, b => b);
        book.Save([new Contact("Ali Veli", ["0532 000 00 00"], ["ali@example.com"])]);
        var plugin = new ContactsPlugin(book, _ => { }, () => true, keyword: ContactsPlugin.Keyword);

        var row = Assert.Single(await plugin.QueryAsync(new Query("/kisi ali", "/kisi", "ali"), CancellationToken.None));
        Assert.Equal(DragPayload.Text("0532 000 00 00"), row.DragData);
    }

    [Fact]
    public async Task Emoji_drags_the_character()
    {
        var plugin = new EmojiPlugin(_ => { });
        var row = (await plugin.QueryAsync(new Query("/e kalp", "/e", "kalp"), CancellationToken.None)).First();
        Assert.Equal(DragKind.Text, row.DragData!.Kind);
        Assert.True(row.DragData.Value.Length <= 4);
    }
}
