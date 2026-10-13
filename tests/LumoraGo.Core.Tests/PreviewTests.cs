using LumoraGo.Core.Files;
using LumoraGo.Core.Preview;

namespace LumoraGo.Core.Tests;

public sealed class PreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumorago-preview-" + Guid.NewGuid().ToString("N"));

    public PreviewTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Text_files_show_their_head_with_details()
    {
        var model = PreviewBuilder.ForPath(Write("notlar.md", "# Başlık\nikinci satır"));

        Assert.Equal(PreviewKind.Text, model.Kind);
        Assert.Equal("notlar.md", model.Title);
        Assert.Equal("# Başlık\nikinci satır", model.Text);
        Assert.False(model.Truncated);
        Assert.Contains(model.Details, d => d.Label == "Boyut" && d.Value.EndsWith("bayt"));
        Assert.Contains(model.Details, d => d.Label == "Konum" && d.Value == _root);
    }

    [Fact]
    public void Long_text_is_cut_at_the_line_limit()
    {
        var lines = string.Join("\n", Enumerable.Range(1, PreviewBuilder.MaxTextLines + 50).Select(i => $"satır {i}"));
        var model = PreviewBuilder.ForPath(Write("uzun.log", lines));

        Assert.True(model.Truncated);
        Assert.Equal(PreviewBuilder.MaxTextLines, model.Text!.Split('\n').Length);
    }

    [Fact]
    public void Unknown_extension_is_sniffed_for_text()
    {
        Assert.Equal(PreviewKind.Text, PreviewBuilder.ForPath(Write("ayar.özel", "anahtar=değer")).Kind);

        var binary = Path.Combine(_root, "veri.özel");
        File.WriteAllBytes(binary, [1, 2, 0, 4, 5]);
        var model = PreviewBuilder.ForPath(binary);
        Assert.Equal(PreviewKind.Info, model.Kind);
        Assert.Null(model.Text);
    }

    [Theory]
    [InlineData("foto.JPG", PreviewKind.Image)]
    [InlineData("rapor.pdf", PreviewKind.Document)]
    [InlineData("sunum.pptx", PreviewKind.Document)]
    [InlineData("klip.mp4", PreviewKind.Media)]
    public void Kind_follows_the_extension(string name, PreviewKind kind)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [0, 1, 2]);
        Assert.Equal(kind, PreviewBuilder.ForPath(path).Kind);
    }

    [Fact]
    public void Folders_list_subfolders_first_with_counts()
    {
        var folder = Path.Combine(_root, "Proje");
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "b.txt"), "12345");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "1");

        var model = PreviewBuilder.ForPath(folder);

        Assert.Equal(PreviewKind.Folder, model.Kind);
        Assert.Equal("Proje", model.Title);
        Assert.Equal(["📁 src", "    a.txt", "    b.txt"], model.Text!.Split('\n'));
        Assert.Contains(("İçerik", "1 klasör, 2 dosya"), model.Details);
        Assert.Contains(("Dosyaların boyutu", "6 bayt"), model.Details);
    }

    [Fact]
    public void Big_folders_are_cut()
    {
        for (var i = 0; i < PreviewBuilder.MaxFolderItems + 5; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i:000}.txt"), "");
        }

        var model = PreviewBuilder.ForPath(_root);
        Assert.True(model.Truncated);
        Assert.Equal(PreviewBuilder.MaxFolderItems, model.Text!.Split('\n').Length);
    }

    [Fact]
    public void Missing_file_says_so()
    {
        var model = PreviewBuilder.ForPath(Path.Combine(_root, "yok.txt"));
        Assert.Equal(PreviewKind.Info, model.Kind);
        Assert.Contains(("Durum", "Dosya bulunamadı"), model.Details);
    }

    [Fact]
    public void Plain_text_results_preview_as_notes()
    {
        var model = PreviewBuilder.ForText("Pano", "uzun bir\nmetin");
        Assert.Equal(PreviewKind.Note, model.Kind);
        Assert.Equal("uzun bir\nmetin", model.Text);
        Assert.Contains(("Uzunluk", "14 karakter"), model.Details);
    }

    [Theory]
    [InlineData(512, "512 bayt")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void Sizes_are_written_in_turkish(long bytes, string expected) => Assert.Equal(expected, PreviewBuilder.Size(bytes));

    [Fact]
    public void File_results_carry_their_preview_path()
    {
        var result = new FileResults(_ => { }, _ => { }).Create(@"C:\a\b.txt", false, "b.txt", "C:\\a", 10);
        Assert.Equal(@"C:\a\b.txt", result.PreviewPath);
    }
}
