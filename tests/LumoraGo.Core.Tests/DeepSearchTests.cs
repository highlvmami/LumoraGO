using System.Text;
using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class DeepSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumorago-ds-" + Guid.NewGuid().ToString("N"));

    public DeepSearchTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private async Task<DeepSearchSnapshot> Run(string term, Func<DeepSearchOptions, DeepSearchOptions>? configure = null)
    {
        var options = new DeepSearchOptions { Roots = [_root] };
        var job = DeepSearchJob.Start(term, configure?.Invoke(options) ?? options);
        await job.Completion;
        return job.Snapshot();
    }

    [Fact]
    public async Task Finds_term_in_names_and_contents_ignoring_turkish_letters()
    {
        var named = Write("belgeler/şifreler.txt", "boş");
        var inside = Write("notlar/hesap.md", "kullanıcı adı: mami\nŞİFRE: 1234\nson satır");
        Write("notlar/alakasız.txt", "burada yok");

        var s = await Run("sifre");

        Assert.False(s.IsRunning);
        Assert.Equal(3, s.FilesScanned);
        Assert.Contains(s.Hits, h => h.Path == named && h.Kind == DeepSearchMatchKind.Name);
        var content = Assert.Single(s.Hits, h => h.Kind == DeepSearchMatchKind.Content);
        Assert.Equal(inside, content.Path);
        Assert.Equal(2, content.Line);
        Assert.Equal("ŞİFRE: 1234", content.Snippet);
    }

    [Fact]
    public async Task Skips_binary_files_skipped_folders_and_big_files()
    {
        File.WriteAllBytes(Path.Combine(_root, "veri.txt"), [0x73, 0x69, 0x66, 0x72, 0x65, 0x00, 0x01]);
        Write("node_modules/paket/sifre.js", "sifre");
        Write("resim.png", "sifre");
        Write("buyuk.log", new string('x', 2000) + "sifre");

        var s = await Run("sifre", o => o with { MaxContentBytes = 1000 });

        Assert.Empty(s.Hits);
    }

    [Fact]
    public async Task Reads_utf16_files()
    {
        var path = Write("unicode.txt", "parola ve şifre", Encoding.Unicode);

        var s = await Run("şifre");

        Assert.Equal(path, Assert.Single(s.Hits).Path);
    }

    [Fact]
    public async Task Stops_at_hit_limit()
    {
        for (var i = 0; i < 20; i++)
        {
            Write($"sifre{i}.txt", "");
        }

        var s = await Run("sifre", o => o with { MaxHits = 5 });

        Assert.Equal(5, s.Hits.Count);
        Assert.True(s.HitLimitReached);
    }

    [Fact]
    public void Snippet_is_cut_around_the_match()
    {
        var line = new string('a', 100) + "sifre" + new string('b', 100);
        var snippet = DeepSearchJob.MakeSnippet(line, 100, 5);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("sifre", snippet);
    }

    [Fact]
    public async Task Plugin_starts_search_streams_results_and_restarts_on_new_term()
    {
        var path = Write("a/gizli.txt", "şifrem burada");
        var opened = new List<string>();
        var host = new NullHost();
        var plugin = new DeepSearchPlugin(() => new DeepSearchOptions { Roots = [_root] }, opened.Add, _ => { });
        plugin.Init(new PluginContext(DeepSearchPlugin.Metadata, _root, host));

        var first = await plugin.QueryAsync(new Query("/ds sifre", "/ds", "sifre"), CancellationToken.None);
        Assert.StartsWith("Aranıyor", first[0].Title);

        IReadOnlyList<Result> results = [];
        for (var i = 0; i < 100 && (results.Count < 2 || results[0].Title.StartsWith("Aranıyor")); i++)
        {
            await Task.Delay(50);
            results = await plugin.QueryAsync(new Query("/ds sifre", "/ds", "sifre"), CancellationToken.None);
        }

        Assert.StartsWith("Derin arama", results[0].Title);
        Assert.Equal("gizli.txt", results[1].Title);
        Assert.Contains("Satır 1", results[1].Subtitle);
        Assert.True(results[1].Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal([path], opened);
        lock (host.Refreshes)
        {
            Assert.Contains(DeepSearchPlugin.Keyword, host.Refreshes);
        }

        var other = await plugin.QueryAsync(new Query("/ds parola", "/ds", "parola"), CancellationToken.None);
        Assert.Contains("parola", other[0].Title);
    }

    [Fact]
    public async Task Plugin_shows_hint_for_short_term()
    {
        var plugin = new DeepSearchPlugin(() => throw new InvalidOperationException(), _ => { }, _ => { });
        var results = await plugin.QueryAsync(new Query("/ds s", "/ds", "s"), CancellationToken.None);
        Assert.Equal("Derin arama", Assert.Single(results).Title);
    }
}
