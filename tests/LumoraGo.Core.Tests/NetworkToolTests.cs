using System.Net;
using System.Text;
using LumoraGo.Core.Network;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!.ToString());
        }

        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class NetworkToolTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static NetworkTools Tools(Func<HttpRequestMessage, HttpResponseMessage> http, PingFunc? ping = null, WhoisQuery? whois = null) =>
        new(new HttpClient(new FakeHandler(http)),
            ping ?? ((_, _, _) => Task.FromResult(new PingReplyInfo(true, 10, "ok", "1.2.3.4"))),
            whois ?? ((_, _, _) => Task.FromResult("")),
            TimeSpan.Zero,
            () => Now);

    private static async Task<List<NetLine>> Run(NetLookup lookup, string argument)
    {
        IReadOnlyList<NetLine> last = [];
        await lookup(argument, l => last = l, CancellationToken.None);
        return last.ToList();
    }

    [Theory]
    [InlineData("google.com", "google.com")]
    [InlineData("https://www.Google.com:8080/arama?q=1", "www.google.com")]
    [InlineData("user@host.example.org", "host.example.org")]
    [InlineData("  EXAMPLE.com.  ", "example.com")]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("[2001:4860:4860::8888]:53", "2001:4860:4860::8888")]
    [InlineData("localhost", "localhost")]
    [InlineData("örnek.com", "xn--rnek-4qa.com")]
    public void Normalizes_hosts(string input, string expected)
    {
        Assert.True(NetworkTools.TryNormalizeHost(input, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("iki kelime")]
    [InlineData("-bad.com")]
    [InlineData("a..b.com")]
    [InlineData("bad_host.com")]
    public void Rejects_bad_hosts(string input) => Assert.False(NetworkTools.TryNormalizeHost(input, out _));

    [Fact]
    public async Task Ping_reports_progress_and_summary()
    {
        var calls = 0;
        var tools = Tools(_ => throw new InvalidOperationException(), (_, _, _) =>
            Task.FromResult(++calls == 3 ? new PingReplyInfo(false, 0, "Zaman aşımı", null) : new PingReplyInfo(true, calls * 10, "ok", "1.1.1.1")));

        var reports = new List<List<NetLine>>();
        await tools.PingAsync("https://one.one.one.one/x", l => reports.Add(l.ToList()), CancellationToken.None);

        Assert.Equal(4, reports.Count);
        var final = reports[^1];
        Assert.Equal("Ortalama 23 ms · en az 10 · en çok 40 · kayıp %25", final[0].Title);
        Assert.Equal(["1.1.1.1: 10 ms", "1.1.1.1: 20 ms", "3. yanıt yok", "1.1.1.1: 40 ms"], final.Skip(1).Select(l => l.Title));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.PingAsync("iki kelime", _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task Dns_collects_records_over_https()
    {
        var tools = Tools(request =>
        {
            var type = request.RequestUri!.Query.Split('&').Single(p => p.StartsWith("type=")).Split('=')[1];
            return type switch
            {
                "A" => FakeHandler.Json("""{"Status":0,"Answer":[{"name":"x.com","type":1,"TTL":300,"data":"93.184.216.34"}]}"""),
                "MX" => FakeHandler.Json("""{"Status":0,"Answer":[{"name":"x.com","type":15,"TTL":60,"data":"10 mail.x.com."}]}"""),
                "TXT" => FakeHandler.Json("""{"Status":0,"Answer":[{"name":"x.com","type":16,"TTL":60,"data":"\"v=spf1 -all\""}]}"""),
                _ => FakeHandler.Json("""{"Status":0}"""),
            };
        });

        var lines = await Run(tools.DnsAsync, "x.com");
        Assert.Equal(["A  93.184.216.34", "MX  10 mail.x.com", "TXT  v=spf1 -all"], lines.Select(l => l.Title));
        Assert.Equal("TTL 300 sn", lines[0].Subtitle);
        Assert.Equal("v=spf1 -all", lines[2].Copy);

        var mxOnly = await Run(tools.DnsAsync, "x.com mx");
        Assert.Single(mxOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(tools.DnsAsync, "x.com bilinmez"));
    }

    [Fact]
    public async Task Dns_reverse_and_nxdomain()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Query.Contains("8.8.8.8.in-addr.arpa".Split('.').Reverse().First())
            ? FakeHandler.Json("""{"Status":0,"Answer":[{"name":"x","type":12,"TTL":10,"data":"dns.google."}]}""")
            : FakeHandler.Json("""{"Status":3}"""));
        var tools = new NetworkTools(new HttpClient(handler), (_, _, _) => Task.FromResult(new PingReplyInfo(true, 1, "", null)), (_, _, _) => Task.FromResult(""));

        var reverse = await Run(tools.DnsAsync, "8.8.8.8");
        Assert.Equal("PTR  dns.google", reverse.Single().Title);
        Assert.Contains("name=8.8.8.8.in-addr.arpa", handler.Requests.Single());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(tools.DnsAsync, "yok.example"));
        Assert.Equal("Alan adı bulunamadı.", error.Message);
    }

    private const string RdapJson = """
        {"ldhName":"EXAMPLE.COM","status":["client delete prohibited"],
         "events":[{"eventAction":"registration","eventDate":"1995-08-14T04:00:00Z"},{"eventAction":"expiration","eventDate":"2026-10-23T04:00:00Z"}],
         "entities":[{"roles":["registrar"],"vcardArray":["vcard",[["version",{},"text","4.0"],["fn",{},"text","IANA Registrar"]]]}],
         "nameservers":[{"ldhName":"A.IANA-SERVERS.NET"}]}
        """;

    [Fact]
    public async Task Whois_uses_rdap_and_walks_up_to_registered_domain()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/www.example.com")
            ? FakeHandler.Json("{}", HttpStatusCode.NotFound)
            : FakeHandler.Json(RdapJson));
        var tools = new NetworkTools(new HttpClient(handler), (_, _, _) => Task.FromResult(new PingReplyInfo(true, 1, "", null)), (_, _, _) => Task.FromResult(""), utcNow: () => Now);

        var lines = await Run(tools.WhoisAsync, "https://www.example.com/a");
        Assert.Equal(["example.com", "IANA Registrar", "14.08.1995", "23.10.2026 (9 gün kaldı)", "client delete prohibited", "a.iana-servers.net"], lines.Select(l => l.Title));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Whois_falls_back_to_classic_whois_for_tlds_without_rdap()
    {
        var queries = new List<(string Server, string Query)>();
        var tools = Tools(
            _ => FakeHandler.Json("{}", HttpStatusCode.NotFound),
            whois: (server, query, _) =>
            {
                queries.Add((server, query));
                return Task.FromResult(server == "whois.iana.org"
                    ? "refer:        whois.nic.example\n"
                    : query == "firma.com.tr"
                        ? "** Domain Name: firma.com.tr\n\n% yorum\nCreated on..: 2010-Jan-01.\nExpires on..: 2030-Jan-01.\n"
                        : "No match found");
            });

        var lines = await Run(tools.WhoisAsync, "firma.com.tr");
        Assert.Equal(["firma.com.tr", "2010-Jan-01.", "2030-Jan-01."], lines.Select(l => l.Title));
        Assert.Equal("Created on..", lines[1].Subtitle);
        Assert.Equal(("whois.iana.org", "tr"), queries[0]);
        Assert.Equal(("whois.nic.example", "firma.com.tr"), queries[1]);
    }

    [Fact]
    public async Task My_ip_parses_primary_and_falls_back()
    {
        var tools = Tools(request => request.RequestUri!.Host == "ipwho.is"
            ? FakeHandler.Json("""{"success":true,"ip":"85.1.2.3","city":"İstanbul","region":"İstanbul","country":"Türkiye","connection":{"isp":"Turk Telekom"},"timezone":{"id":"Europe/Istanbul"}}""")
            : throw new InvalidOperationException());
        var lines = await Run((_, report, ct) => tools.MyIpAsync(report, ct), "");
        Assert.Equal(["85.1.2.3", "İstanbul, İstanbul, Türkiye", "Turk Telekom", "Europe/Istanbul"], lines.Select(l => l.Title));

        var fallback = Tools(request => request.RequestUri!.Host == "ipwho.is"
            ? FakeHandler.Json("""{"success":false}""")
            : FakeHandler.Json("""{"ip":"9.9.9.9","city":"Berlin","country_name":"Germany","org":"Quad9"}"""));
        var viaFallback = await Run((_, report, ct) => fallback.MyIpAsync(report, ct), "");
        Assert.Equal(["9.9.9.9", "Berlin, Germany", "Quad9"], viaFallback.Select(l => l.Title));
    }

    [Fact]
    public async Task Speed_test_reports_all_phases()
    {
        var handler = new FakeHandler(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[int.Parse(request.RequestUri!.Query.Split('=')[1])]),
            });
        var speed = new SpeedTest(new HttpClient(handler)) { DownloadBytes = 300_000, UploadBytes = 100_000 };

        var reports = new List<List<NetLine>>();
        await speed.RunAsync(l => reports.Add(l.ToList()), CancellationToken.None);

        var final = reports[^1];
        Assert.Equal(["İndirme hızı", "Yükleme hızı", "Gecikme"], final.Select(l => l.Subtitle));
        Assert.All(final, l => Assert.True(l.Title.EndsWith("Mbps") || l.Title.EndsWith("ms")));
        Assert.True(reports.Count >= 4);
        Assert.Equal("1 Mbps", SpeedTest.Mbps(125_000, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Plugin_starts_on_enter_streams_lines_and_copies()
    {
        var release = new TaskCompletionSource();
        var host = new NullHost();
        var plugin = new NetworkToolPlugin(new PluginMetadata("t.x", "Deneme", "/dn"), "Çalıştır", "/dn bir", async (arg, report, ct) =>
        {
            report([new NetLine($"ilk {arg}")]);
            await release.Task;
            report([new NetLine($"son {arg}", "alt", "kopya")]);
        });
        plugin.Init(new PluginContext(plugin.Metadata, "", host));

        var empty = Assert.Single(await plugin.QueryAsync(new Query("/dn", "/dn", ""), CancellationToken.None));
        Assert.StartsWith("Deneme: bir şey yaz", empty.Title);

        var start = Assert.Single(await plugin.QueryAsync(new Query("/dn Abc", "/dn", "Abc"), CancellationToken.None));
        Assert.Equal("Çalıştır: Abc", start.Title);
        Assert.False(start.Action(new ActionContext(ActionModifiers.None)));

        var running = await Eventually(plugin, "Abc", r => r.Any(x => x.Title == "ilk Abc"));
        Assert.Equal("Çalışıyor…", running[0].Title);

        release.SetResult();
        var done = await Eventually(plugin, "Abc", r => r.Any(x => x.Title == "son Abc") && r[0].Title != "Çalışıyor…");
        done[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["kopya"], host.Copied);
        Assert.Contains("/dn", host.Refreshes);
    }

    [Fact]
    public async Task Plugin_auto_run_and_errors()
    {
        var runs = 0;
        var plugin = new NetworkToolPlugin(new PluginMetadata("t.y", "Otomatik", "/ot"), "Git", "/ot", (_, _, _) =>
        {
            runs++;
            throw new InvalidOperationException("bağlantı yok");
        }, autoRun: true, needsArgument: false);
        plugin.Init(new PluginContext(plugin.Metadata, "", new NullHost()));

        await plugin.QueryAsync(new Query("/ot", "/ot", ""), CancellationToken.None);
        var failed = await Eventually(plugin, "", r => r.Any(x => x.Title == "Hata: bağlantı yok"));
        Assert.NotEmpty(failed);
        await plugin.QueryAsync(new Query("/ot", "/ot", ""), CancellationToken.None);
        Assert.Equal(1, runs);
    }

    private static async Task<List<Result>> Eventually(NetworkToolPlugin plugin, string search, Func<List<Result>, bool> condition)
    {
        var deadline = DateTime.UtcNow + TestPlugins.Timeout;
        while (true)
        {
            var results = (await plugin.QueryAsync(new Query($"{plugin.Metadata.ActionKeyword} {search}".TrimEnd(), plugin.Metadata.ActionKeyword, search), CancellationToken.None)).ToList();
            if (condition(results) || DateTime.UtcNow > deadline)
            {
                return results;
            }

            await Task.Delay(10);
        }
    }
}
