using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Tests;

public sealed class GoogleContactsTests : IDisposable
{
    private const string Page1 = """
        {"connections":[
          {"names":[{"displayName":"Ahmet Yılmaz"}],"phoneNumbers":[{"value":"0532 123 45 67"},{"value":"0532 123 45 67"}],
           "emailAddresses":[{"value":"ahmet@example.com"}],"organizations":[{"name":"Acme"}]},
          {"organizations":[{"name":"Kuaför Ayşe"}],"phoneNumbers":[{"value":"0212 555 00 00"}]},
          {"resourceName":"people/c3"}
        ],"nextPageToken":"p2","totalPeople":3}
        """;

    private const string Page2 = """{"connections":[{"names":[{"displayName":"Zeynep"}],"emailAddresses":[{"value":"z@example.com"}]}]}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-google-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static byte[] Flip(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();

    private ContactBook Book() => new(Path.Combine(_dir, "contacts.dat"), Flip, Flip);

    private SecretStore Secrets() => new(Path.Combine(_dir, "accounts.dat"), Flip, Flip);

    [Fact]
    public void Connections_become_contacts()
    {
        var (contacts, next) = GoogleContacts.ParseConnections(Page1);

        Assert.Equal("p2", next);
        Assert.Equal(2, contacts.Count);
        var ahmet = contacts[0];
        Assert.Equal("Ahmet Yılmaz", ahmet.Name);
        Assert.Equal(["0532 123 45 67"], ahmet.Phones);
        Assert.Equal(["ahmet@example.com"], ahmet.Emails);
        Assert.Equal("Acme", ahmet.Organization);
        Assert.True(ahmet.FromGoogle);
        // Adı olmayan kişi şirket adıyla görünür; hiçbir bilgisi olmayan atlanır.
        Assert.Equal("Kuaför Ayşe", contacts[1].Name);
    }

    [Fact]
    public void Empty_rehber_has_no_connections_key()
    {
        var (contacts, next) = GoogleContacts.ParseConnections("""{"totalPeople":0}""");
        Assert.Empty(contacts);
        Assert.Null(next);
    }

    [Fact]
    public async Task Fetch_follows_pages_with_bearer_token()
    {
        var auth = new List<string?>();
        var handler = new FakeHandler(r =>
        {
            auth.Add(r.Headers.Authorization?.ToString());
            return FakeHandler.Json(r.RequestUri!.Query.Contains("pageToken=p2") ? Page2 : Page1);
        });

        var contacts = await GoogleContacts.FetchAsync(new HttpClient(handler), "tok", CancellationToken.None);

        Assert.Equal(["Ahmet Yılmaz", "Kuaför Ayşe", "Zeynep"], contacts.Select(c => c.Name));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(auth, a => Assert.Equal("Bearer tok", a));
        Assert.Contains("personFields=names,emailAddresses,phoneNumbers,organizations", handler.Requests[0]);
    }

    [Fact]
    public async Task Disabled_people_api_says_how_to_enable()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"SERVICE_DISABLED"}]}}""", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<GoogleException>(() => GoogleContacts.FetchAsync(new HttpClient(handler), "tok", CancellationToken.None));
        Assert.Contains("People API", ex.Message);
    }

    [Fact]
    public async Task Missing_contacts_permission_says_to_tick_the_box()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"error":{"code":403,"status":"PERMISSION_DENIED","details":[{"reason":"ACCESS_TOKEN_SCOPE_INSUFFICIENT"}]}}""", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<GoogleException>(() => GoogleContacts.FetchAsync(new HttpClient(handler), "tok", CancellationToken.None));
        Assert.Contains("kutucu", ex.Message);
    }

    [Fact]
    public void Auth_url_asks_read_only_offline_with_pkce()
    {
        var (verifier, challenge) = GoogleContacts.NewPkce();
        var url = GoogleContacts.AuthUrl("id.apps.googleusercontent.com", "http://127.0.0.1:5000", challenge, "st");

        Assert.Contains("client_id=id.apps.googleusercontent.com", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000", url);
        Assert.Contains("contacts.readonly", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("code_challenge_method=S256", url);
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
        Assert.InRange(verifier.Length, 43, 128);
    }

    private static async Task<string> Browser(int port, string pathAndQuery)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {pathAndQuery} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<(Task<string> Code, int Port, TcpListener Listener)> Listen(string state)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var code = GoogleContacts.ReceiveCodeAsync(listener, state, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        await Task.Yield();
        return (code, port, listener);
    }

    [Fact]
    public async Task Loopback_returns_code_after_ignoring_favicon()
    {
        var (code, port, listener) = await Listen("st");
        using (listener)
        {
            Assert.Contains("404", await Browser(port, "/favicon.ico"));
            var page = await Browser(port, "/?state=st&code=4%2F0Abc&scope=x");
            Assert.Contains("bağlandı", page);
            Assert.Equal("4/0Abc", await code);
        }
    }

    [Fact]
    public async Task Loopback_rejects_wrong_state_and_denial()
    {
        var (code, port, listener) = await Listen("st");
        using (listener)
        {
            await Browser(port, "/?state=other&code=x");
            Assert.Contains("state", (await Assert.ThrowsAsync<GoogleException>(() => code)).Message);
        }

        (code, port, listener) = await Listen("st");
        using (listener)
        {
            await Browser(port, "/?state=st&error=access_denied");
            Assert.Contains("izin verilmedi", (await Assert.ThrowsAsync<GoogleException>(() => code)).Message);
        }
    }

    [Theory]
    [InlineData("""{"error":"invalid_grant"}""", "yeniden")]
    [InlineData("""{"error":"invalid_client"}""", "İstemci kimliği")]
    [InlineData("not json", "bilinmeyen")]
    public void Token_errors_are_explained(string body, string expected) =>
        Assert.Contains(expected, GoogleContacts.TokenError(body));

    [Fact]
    public void Google_contacts_with_emoji_or_broken_characters_are_saved_and_searchable()
    {
        var book = Book();
        book.ReplaceGoogle([new Contact("Annem ❤️😍", ["05321234567"], []), new Contact("Bozuk\uD800 Ad", [], [])]);
        Assert.Equal(2, book.All.Count);
        Assert.Equal("annem ❤️😍", FuzzyMatcher.Fold("Annem ❤️😍"));
    }

    [Fact]
    public void Google_contacts_replace_only_themselves_and_survive_file_import()
    {
        var book = Book();
        book.Save([new Contact("Mehmet", ["05321112233"], []), new Contact("Ahmet Yılmaz", ["05320000000"], [])]);

        book.ReplaceGoogle([new Contact("Ahmet Yılmaz", ["05321234567"], ["ahmet@example.com"]), new Contact("Zeynep", [], ["z@example.com"])]);
        // Dosyadaki aynı adlı Ahmet atılır, Google'daki kalır.
        Assert.Equal(["Mehmet", "Ahmet Yılmaz", "Zeynep"], book.All.Select(c => c.Name));
        Assert.Equal(["05321234567"], book.All.Single(c => c.Name == "Ahmet Yılmaz").Phones);

        book.ReplaceGoogle([new Contact("Zeynep", [], ["z@example.com"])]);
        Assert.Equal(["Mehmet", "Zeynep"], book.All.Select(c => c.Name));

        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "k.vcf");
        System.IO.File.WriteAllText(file, "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ali\r\nTEL:05330000000\r\nEND:VCARD\r\n");
        book.Import(file);
        Assert.Equal(["Ali", "Zeynep"], Book().All.Select(c => c.Name));

        book.RemoveGoogle();
        Assert.Equal(["Ali"], book.All.Select(c => c.Name));
    }

    [Fact]
    public async Task Sync_refreshes_token_and_fills_book()
    {
        var secrets = Secrets();
        var book = Book();
        var http = new HttpClient(new FakeHandler(r => r.RequestUri!.Host == "oauth2.googleapis.com"
            ? FakeHandler.Json("""{"access_token":"fresh","expires_in":3599}""")
            : r.Headers.Authorization?.Parameter == "fresh" ? FakeHandler.Json(Page2) : FakeHandler.Json("{}", HttpStatusCode.Unauthorized)));
        var sync = new GoogleContactSync(http, secrets, book);

        Assert.Null(await sync.SyncAsync(CancellationToken.None));

        secrets.Save(secrets.Current with { Google = new GoogleAccount("id", "secret", "refresh", "me@gmail.com") });
        Assert.Equal(1, await sync.SyncAsync(CancellationToken.None));
        Assert.Equal("Zeynep", Assert.Single(book.All).Name);
        Assert.True(book.All[0].FromGoogle);
    }

    [Fact]
    public async Task Revoked_access_asks_to_reconnect()
    {
        var secrets = Secrets();
        secrets.Save(secrets.Current with { Google = new GoogleAccount("id", "secret", "refresh") });
        var http = new HttpClient(new FakeHandler(_ => FakeHandler.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)));

        var ex = await Assert.ThrowsAsync<GoogleException>(() => new GoogleContactSync(http, secrets, Book()).SyncAsync(CancellationToken.None));
        Assert.Contains("yeniden", ex.Message);
    }
}
