using System.Text.Json;
using LumoraGo.Core.Desktop;
using LumoraGo.Core.Google;
using LumoraGo.Core.Info;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

/// <summary>/haber, /film, /çevir, /gmail, /sistem, /temizle ve /başlangıç.</summary>
public class PackThreeTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><title>BBC</title>
          <item><title><![CDATA[Deprem: <b>İzmir</b> sallandı]]></title><link>https://bbc.com/a</link>
            <pubDate>Wed, 14 Oct 2026 21:05:00 GMT</pubDate><description>&lt;p&gt;Kandilli &amp;amp; AFAD&lt;/p&gt;</description></item>
          <item><title>Eski haber</title><link>https://bbc.com/b</link><pubDate>Tue, 13 Oct 2026 08:00:00 +0300</pubDate></item>
          <item><title></title><link>https://bbc.com/bos</link></item>
        </channel></rss>
        """;

    private const string Atom = """
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Webtekno</title>
          <entry><title>Yeni telefon</title><link rel="alternate" href="https://webtekno.com/x"/><updated>2026-10-15T00:30:00+03:00</updated><summary>Özet</summary></entry>
        </feed>
        """;

    private const string Rdf = """
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <item rdf:about="https://dw.com/1"><title>DW haberi</title><link>https://dw.com/1</link><dc:date>2026-10-14T19:00:00Z</dc:date></item>
        </rdf:RDF>
        """;

    [Fact]
    public void News_ParsesRssAtomAndRdf()
    {
        var rss = NewsService.Parse("BBC", Rss);
        Assert.Equal(2, rss.Count);
        Assert.Equal("Deprem: İzmir sallandı", rss[0].Title);
        Assert.Equal("Kandilli & AFAD", rss[0].Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 21, 5, 0, TimeSpan.Zero).LocalDateTime, rss[0].Published);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 5, 0, 0, TimeSpan.Zero).LocalDateTime, rss[1].Published);

        var atom = Assert.Single(NewsService.Parse("Webtekno", Atom));
        Assert.Equal("https://webtekno.com/x", atom.Url);
        Assert.Equal("Özet", atom.Summary);

        var rdf = Assert.Single(NewsService.Parse("DW", Rdf));
        Assert.Equal("https://dw.com/1", rdf.Url);
        Assert.NotNull(rdf.Published);
    }

    [Theory]
    [InlineData("Wed, 14 Oct 2026 17:39:32 Z", 17)]
    [InlineData("Thu, 15 Oct 2026 01:46:48 +0300", -1)]
    [InlineData("2026-10-15T01:46:31+03:00", -1)]
    [InlineData("Wed, 14 Oct 2026 21:30:45 +0000", 21)]
    public void News_ReadsRealWorldDates(string text, int utcHour)
    {
        var date = NewsService.Date(text);
        Assert.NotNull(date);
        Assert.Equal(utcHour < 0 ? 22 : utcHour, date!.Value.ToUniversalTime().Hour);
    }

    [Fact]
    public void NewsFeed_ParsesSettingsLine()
    {
        Assert.Equal(new NewsFeed("NTV", "https://ntv.com.tr/a.rss"), NewsFeed.Parse("NTV | https://ntv.com.tr/a.rss"));
        Assert.Equal("ntv.com.tr", NewsFeed.Parse("https://www.ntv.com.tr/a.rss")!.Name);
        Assert.Null(NewsFeed.Parse("NTV | ntv"));
        Assert.All(NewsFeed.Defaults, l => Assert.NotNull(NewsFeed.Parse(l)));
    }

    [Fact]
    public async Task News_MergesNewestFirstFiltersBySourceAndReportsFailures()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host switch
        {
            "bbc.test" => Xml(Rss),
            "tekno.test" => Xml(Atom),
            _ => FakeHandler.Json("", System.Net.HttpStatusCode.InternalServerError),
        });
        var news = new NewsService(new HttpClient(handler));
        NewsFeed[] feeds = [new("BBC", "https://bbc.test/rss"), new("Webtekno", "https://tekno.test/rss"), new("Bozuk", "https://bad.test/rss")];
        var now = new DateTime(2026, 10, 15, 0, 0, 0);

        var all = await WebInfoPlugins.NewsAsync(news, feeds, "", _ => { }, now, CancellationToken.None);
        Assert.Equal("Yeni telefon", all[0].Title);
        Assert.Equal("Deprem: İzmir sallandı", all[1].Title);
        Assert.Contains("Bozuk", all[^1].Subtitle);

        var bbc = await WebInfoPlugins.NewsAsync(news, feeds, "bbc", _ => { }, now, CancellationToken.None);
        Assert.All(bbc, r => Assert.StartsWith("BBC", r.Subtitle));

        var word = await WebInfoPlugins.NewsAsync(news, feeds, "deprem", _ => { }, now, CancellationToken.None);
        Assert.Equal("Deprem: İzmir sallandı", word[0].Title);
        Assert.Single(handler.Requests, u => u.Contains("bbc.test"));
    }

    private static HttpResponseMessage Xml(string xml) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(xml) };

    [Fact]
    public void Translator_ParsesNestedArrays()
    {
        var t = Translator.Parse("""[[["Selam ","hello ",null,null,10],["Dünya","world",null,null,10]],null,"en",null]""", "tr");
        Assert.Equal("Selam Dünya", t.Text);
        Assert.Equal("en", t.From);
    }

    [Fact]
    public async Task Translate_TurkishGoesToEnglishAndClipboardIsUsedWhenEmpty()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("tl=tr")
            ? FakeHandler.Json("""[[["merhaba","merhaba"]],null,"tr"]""")
            : FakeHandler.Json("""[[["hello","merhaba"]],null,"tr"]"""));
        var translator = new Translator(new HttpClient(handler));
        var host = new NullHost();
        var pasted = new List<string>();

        var results = await WebInfoPlugins.TranslateAsync(translator, "", () => "merhaba", host, pasted.Add, _ => { }, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Equal("hello", result.Title);
        Assert.Contains("Türkçe → İngilizce (panodan)", result.Subtitle);
        result.Action(new ActionContext(ActionModifiers.Ctrl));
        Assert.Equal(["hello"], pasted);

        Assert.Equal(("de", "günaydın"), WebInfoPlugins.SplitLanguage("de günaydın"));
        Assert.Equal((null, "deniz mavi"), WebInfoPlugins.SplitLanguage("deniz mavi"));
    }

    [Fact]
    public async Task Movies_ShowRatingGenresAndTurkishPlatforms()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.Contains("search")
            ? FakeHandler.Json("""
                {"results":[
                  {"id":27205,"media_type":"movie","title":"Başlangıç","original_title":"Inception","release_date":"2010-07-15","vote_average":8.37,"vote_count":36000,"overview":"Rüya hırsızı."},
                  {"id":1,"media_type":"person","name":"Leo"},
                  {"id":1396,"media_type":"tv","name":"Breaking Bad","original_name":"Breaking Bad","first_air_date":"2008-01-20","vote_average":8.9,"vote_count":15000,"overview":""}]}
                """)
            : r.RequestUri!.AbsolutePath.Contains("movie/27205")
                ? FakeHandler.Json("""
                    {"genres":[{"name":"Aksiyon"},{"name":"Bilim Kurgu"}],
                     "watch/providers":{"results":{"TR":{"flatrate":[{"provider_name":"Netflix"}],"rent":[{"provider_name":"Apple TV"}]}}}}
                    """)
                : FakeHandler.Json("""{"genres":[{"name":"Dram"}],"overview":"Kimya öğretmeni.","watch/providers":{"results":{}}}"""));
        var movies = new MovieService(new HttpClient(handler), () => "abc123");

        var results = await WebInfoPlugins.MoviesAsync(movies, "inception", _ => { }, () => { }, CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.Equal("Başlangıç (2010)", results[0].Title);
        Assert.Equal("★ 8,4", results[0].Card!.Headline);
        Assert.Equal("Film · Inception · Aksiyon, Bilim Kurgu", results[0].Card!.Caption);
        Assert.Contains("Türkiye'de: Netflix", results[0].Card!.Lines!);
        Assert.Contains("Kirala / satın al: Apple TV", results[0].Card!.Lines!);
        Assert.Contains("Kimya öğretmeni.", results[1].Card!.Lines!);
        Assert.Contains("api_key=abc123", handler.Requests[0]);
        Assert.Contains("language=tr-TR", handler.Requests[0]);
    }

    [Fact]
    public async Task Movies_WithoutKeyPointsToSettings()
    {
        var movies = new MovieService(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), () => null);
        var opened = false;
        var results = await WebInfoPlugins.MoviesAsync(movies, "x", _ => { }, () => opened = true, CancellationToken.None);
        Assert.Contains("TMDB", results[0].Title);
        results[0].Action(new ActionContext(ActionModifiers.None));
        Assert.True(opened);
    }

    [Fact]
    public void Gmail_ParsesMetadataMessage()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"m1","threadId":"t1","labelIds":["INBOX","UNREAD"],"snippet":"Fatura tutar&#39;ı hazır","internalDate":"1792000000000",
             "payload":{"headers":[{"name":"From","value":"\"Türk Telekom\" <fatura@tt.com.tr>"},{"name":"Subject","value":"Ekim faturası"}]}}
            """);
        var m = Gmail.ParseMessage(doc.RootElement)!;
        Assert.Equal("Türk Telekom", m.From);
        Assert.Equal("fatura@tt.com.tr", m.FromAddress);
        Assert.Equal("Ekim faturası", m.Subject);
        Assert.Equal("Fatura tutar'ı hazır", m.Snippet);
        Assert.True(m.Unread);
        Assert.Equal("https://mail.google.com/mail/u/0/#all/t1", m.Url);
        Assert.Equal(("ali@x.com", "ali@x.com"), Gmail.SplitFrom("ali@x.com"));
    }

    [Fact]
    public async Task GmailWatcher_SeedsFirstThenReportsOnlyNew()
    {
        var batches = new Queue<IReadOnlyList<GmailMessage>>([
            [Mail("a"), Mail("b")],
            [Mail("c"), Mail("a"), Mail("b")],
            [Mail("c")],
        ]);
        var watcher = new GmailWatcher(_ => Task.FromResult(batches.Dequeue()));
        Assert.Empty(await watcher.CheckAsync(CancellationToken.None));
        Assert.Equal(["c"], (await watcher.CheckAsync(CancellationToken.None)).Select(m => m.Id));
        Assert.Empty(await watcher.CheckAsync(CancellationToken.None));
    }

    private static GmailMessage Mail(string id) => new(id, id, "Ali", "ali@x.com", "Konu " + id, "", null, true);

    [Fact]
    public void FolderCleaner_DeletesOnlyOldFilesAndEmptyFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "lumorago-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "eski"));
            var old = Path.Combine(root, "eski", "a.tmp");
            File.WriteAllBytes(old, new byte[1000]);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            Directory.SetLastWriteTimeUtc(Path.Combine(root, "eski"), DateTime.UtcNow.AddDays(-3));
            var fresh = Path.Combine(root, "yeni.tmp");
            File.WriteAllBytes(fresh, new byte[500]);

            Assert.Equal(1000, FolderCleaner.Measure([root], TimeSpan.FromDays(1)));
            Assert.Equal(1500, FolderCleaner.Measure([root, Path.Combine(root, "yok")], TimeSpan.Zero));
            var outcome = FolderCleaner.Clean([root], TimeSpan.FromDays(1));
            Assert.Equal(new CleanupOutcome(1000, 0), outcome);
            Assert.False(File.Exists(old));
            Assert.False(Directory.Exists(Path.Combine(root, "eski")));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Cleanup_ListsSizesAndCleansInBackground()
    {
        var cleaned = new List<string>();
        var opened = false;
        CleanupTarget[] targets =
        [
            new("Geçici dosyalar", "Temp", "🗑️", () => 2L << 30, () => { cleaned.Add("temp"); return new CleanupOutcome(2L << 30, 3); }),
            new("Geri Dönüşüm Kutusu", "Çöp", "♻️", () => 0, () => { cleaned.Add("bin"); return new CleanupOutcome(0, 0); }),
            new("Tarayıcı önbellekleri", "Cache", "🌐", () => 300L << 20, () => { cleaned.Add("cache"); return new CleanupOutcome(300L << 20, 0); }),
            new("İndirilenler", "Açılır", "📥", () => 5L << 30, null, () => opened = true),
        ];
        var host = new NullHost();
        var results = SystemPlugins.Cleanup(targets, host);
        Assert.StartsWith("Hepsini temizle · ", results[0].Title);
        Assert.Contains("Geçici dosyalar, Tarayıcı önbellekleri", results[0].Subtitle);
        Assert.Equal("Geri Dönüşüm Kutusu · boş", results[2].Title);
        Assert.EndsWith("Enter ile aç", results[4].Subtitle);

        results[0].Action(new ActionContext(ActionModifiers.None));
        for (var i = 0; i < 100 && host.Messages.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(["temp", "cache"], cleaned);
        Assert.Contains("3 dosya atlandı", Assert.Single(host.Messages).Message);
        results[4].Action(new ActionContext(ActionModifiers.None));
        Assert.True(opened);
    }

    [Fact]
    public async Task System_ShowsCardsDisksBatteryAndTopProcesses()
    {
        var snapshot = new SystemSnapshot(92.4, 16L << 30, 6L << 30,
            [new DiskUsage("C:", "Windows", 500L << 30, 25L << 30)], new BatteryStatus(80, false, TimeSpan.FromMinutes(150)),
            TimeSpan.FromHours(30), [new RunningProcess(1, "chrome", 2L << 30, null)]);
        var results = await SystemPlugins.SystemAsync(_ => Task.FromResult(snapshot), new NullHost(), () => { }, CancellationToken.None);
        Assert.Equal("%92", results[0].Card!.Headline);
        Assert.False(results[0].Card!.Positive);
        Assert.Contains("1 gün 6 sa", results[0].Card!.Lines![0]);
        Assert.Equal("6,0 GB / 16,0 GB", results[1].Card!.Headline);
        Assert.Contains("/temizle", results[2].Subtitle);
        Assert.Equal("Pil %80 · yaklaşık 2 sa 30 dk kaldı", results[3].Title);
        Assert.StartsWith("chrome · 2", results[4].Title);
    }

    [Fact]
    public async Task Startup_TogglesAndShowsState()
    {
        var items = new List<StartupItem>
        {
            new("Discord", "\"C:\\Discord.exe\"", "Kayıt defteri (bu kullanıcı)", true, false, null),
            new("Steam", "steam.exe -silent", "Kayıt defteri (bu kullanıcı)", false, false, null),
            new("Adobe", "x.exe", "Kayıt defteri (tüm kullanıcılar)", true, true, null),
        };
        var calls = new List<(string, bool)>();
        var settingsOpened = false;
        var plugin = new StartupPlugin("/başlangıç", () => items, (item, on) =>
        {
            calls.Add((item.Name, on));
            return item.NeedsAdmin ? "yönetici izni gerekiyor" : null;
        }, () => settingsOpened = true);
        var host = new NullHost();
        plugin.Init(new PluginContext(plugin.Metadata, "", host));

        var results = await plugin.QueryAsync(new Query("/başlangıç", "/başlangıç", ""), CancellationToken.None);
        Assert.Equal(["Adobe", "Discord", "Steam"], results.Take(3).Select(r => r.Title));
        Assert.StartsWith("Kapalı · Enter ile aç", results[2].Subtitle);
        Assert.False(results[1].Action(new ActionContext(ActionModifiers.None)));
        Assert.Contains("/başlangıç", host.Refreshes);
        Assert.True(results[0].Action(new ActionContext(ActionModifiers.None)));
        Assert.True(settingsOpened);
        Assert.Equal([("Discord", false), ("Adobe", false)], calls);

        var filtered = await plugin.QueryAsync(new Query("/başlangıç ste", "/başlangıç", "ste"), CancellationToken.None);
        Assert.Equal("Steam", filtered[0].Title);
    }
}
