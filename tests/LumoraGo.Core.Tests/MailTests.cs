using System.Net;
using System.Net.Sockets;
using System.Text;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Mail;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

/// <summary>Tek bağlantı kabul eden, komutları kaydeden ve hazır yanıt veren sahte IMAP sunucusu.</summary>
internal sealed class FakeImapServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _loop;

    public FakeImapServer(string user, string password, int[] unseenUids, Dictionary<int, string> headers)
    {
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
                {
                    return;
                }

                _ = Task.Run(() => Serve(client, user, password, unseenUids, headers));
            }
        });
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = [];

    public ImapConnect Connect => async (host, port, ct) =>
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port, ct);
        return tcp.GetStream();
    };

    private async Task Serve(TcpClient client, string user, string password, int[] unseen, Dictionary<int, string> headers)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        async Task Send(string text) => await stream.WriteAsync(Encoding.UTF8.GetBytes(text));

        await Send("* OK IMAP4rev1 hazır\r\n");
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (Commands)
            {
                Commands.Add(line);
            }

            var parts = line.Split(' ', 3);
            var tag = parts[0];
            var command = parts[1].ToUpperInvariant();
            var rest = parts.Length > 2 ? parts[2] : "";
            switch (command)
            {
                case "LOGIN":
                    await Send(rest == $"\"{user}\" \"{password}\""
                        ? $"{tag} OK LOGIN tamam\r\n"
                        : $"{tag} NO [AUTHENTICATIONFAILED] Invalid credentials\r\n");
                    break;
                case "EXAMINE":
                    await Send($"* 42 EXISTS\r\n{tag} OK [READ-ONLY] EXAMINE tamam\r\n");
                    break;
                case "UID" when rest.StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase):
                    await Send($"* SEARCH {string.Join(' ', unseen)}\r\n{tag} OK SEARCH tamam\r\n".Replace("* SEARCH \r\n", "* SEARCH\r\n"));
                    break;
                case "UID" when rest.StartsWith("FETCH", StringComparison.OrdinalIgnoreCase):
                    var set = rest.Split(' ')[1].Split(',').Select(int.Parse).ToList();
                    var seq = 1;
                    foreach (var uid in set)
                    {
                        var block = headers[uid];
                        await Send($"* {seq++} FETCH (UID {uid} BODY[HEADER.FIELDS (FROM SUBJECT DATE)] {{{Encoding.UTF8.GetByteCount(block)}}}\r\n{block})\r\n");
                    }

                    await Send($"{tag} OK FETCH tamam\r\n");
                    break;
                case "LOGOUT":
                    await Send($"* BYE\r\n{tag} OK LOGOUT tamam\r\n");
                    return;
                default:
                    await Send($"{tag} BAD bilinmeyen komut\r\n");
                    break;
            }
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        _loop.Wait(TimeSpan.FromSeconds(2));
    }
}

public class MailTests
{
    private static readonly MailAccount Account = new("imap.example.com", 993, "mami@example.com", "uygulama sifresi");

    private static Dictionary<int, string> Headers => new()
    {
        [11] = "From: Ali Veli <ali@example.com>\r\nSubject: Toplantı notları\r\nDate: Tue, 13 Oct 2026 09:30:00 +0300\r\n\r\n",
        [12] = "From: =?UTF-8?B?w5Z6bGVtIEHEn2Fj?= <ozlem@example.com>\r\nSubject: =?iso-8859-9?Q?G=FCn=FCn_=FDnsanlar=FD?=\r\n =?UTF-8?B?IGhhYmVybGVy?=\r\nDate: Tue, 13 Oct 2026 10:00:00 +0300 (+03)\r\n\r\n",
        [13] = "From: bildirim@example.com\r\nSubject:\r\n\r\n",
    };

    [Fact]
    public async Task Fetches_unread_newest_first_and_never_marks_read()
    {
        using var server = new FakeImapServer(Account.Address, Account.Password, [11, 12, 13], Headers);

        var summary = await ImapClient.FetchUnreadAsync(server.Connect, Account, 20, CancellationToken.None);

        Assert.Equal(3, summary.Unread);
        Assert.Equal([13L, 12L, 11L], summary.Latest.Select(h => h.Uid));
        Assert.Equal("bildirim@example.com", summary.Latest[0].From);
        Assert.Equal("(konu yok)", summary.Latest[0].Subject);
        Assert.Equal("Özlem Ağac", summary.Latest[1].From);
        Assert.Equal("Günün ınsanları haberler", summary.Latest[1].Subject);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(3)), summary.Latest[1].Date);
        Assert.Equal("Ali Veli", summary.Latest[2].From);
        Assert.Equal("Toplantı notları", summary.Latest[2].Subject);

        // Salt okunur açılır, gövde PEEK ile bile istenmez, böylece mailler okundu olmaz.
        Assert.Contains(server.Commands, c => c.EndsWith("EXAMINE INBOX"));
        Assert.DoesNotContain(server.Commands, c => c.Contains(" SELECT ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.Contains("BODY[", StringComparison.Ordinal) && !c.Contains("PEEK[HEADER.FIELDS"));
        Assert.DoesNotContain(server.Commands, c => c.Contains("STORE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(server.Commands, c => c.EndsWith("LOGOUT"));
    }

    [Fact]
    public async Task Asks_only_for_the_newest_few_headers()
    {
        using var server = new FakeImapServer(Account.Address, Account.Password, [11, 12, 13], Headers);

        var summary = await ImapClient.FetchUnreadAsync(server.Connect, Account, 2, CancellationToken.None);

        Assert.Equal(3, summary.Unread);
        Assert.Equal([13L, 12L], summary.Latest.Select(h => h.Uid));
        Assert.Contains(server.Commands, c => c.Contains("UID FETCH 12,13 "));
    }

    [Fact]
    public async Task No_unread_mail_returns_empty_without_fetching()
    {
        using var server = new FakeImapServer(Account.Address, Account.Password, [], Headers);

        var summary = await ImapClient.FetchUnreadAsync(server.Connect, Account, 20, CancellationToken.None);

        Assert.Equal(0, summary.Unread);
        Assert.Empty(summary.Latest);
        Assert.DoesNotContain(server.Commands, c => c.Contains("FETCH"));
    }

    [Fact]
    public async Task Wrong_password_explains_app_passwords()
    {
        using var server = new FakeImapServer(Account.Address, "başka", [11], Headers);

        var ex = await Assert.ThrowsAsync<ImapException>(() => ImapClient.FetchUnreadAsync(server.Connect, Account, 20, CancellationToken.None));

        Assert.Contains("giriş reddedildi", ex.Message);
        Assert.Contains("uygulama şifresi", ex.Message);
    }

    [Fact]
    public async Task Unreachable_server_is_a_friendly_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var ex = await Assert.ThrowsAsync<ImapException>(() => ImapClient.FetchUnreadAsync(
            async (_, _, ct) =>
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
                return tcp.GetStream();
            }, Account, 20, CancellationToken.None));

        Assert.StartsWith("sunucuya bağlanılamadı", ex.Message);
    }

    [Theory]
    [InlineData("=?UTF-8?B?w5Z6bGVtIEHEn2Fj?=", "Özlem Ağac")]
    [InlineData("=?iso-8859-9?Q?G=FCn=FCn_=FDnsanlar=FD?=", "Günün ınsanları")]
    [InlineData("=?UTF-8?Q?=C5=9Eeker?= =?UTF-8?Q?_bayram=C4=B1?=", "Şeker bayramı")]
    [InlineData("Merhaba =?UTF-8?B?w7zDp8O8bmPDvA==?= dünya", "Merhaba üçüncü dünya")]
    [InlineData("düz metin", "düz metin")]
    [InlineData("=?bilinmeyen-set?B?AAAA?=", "=?bilinmeyen-set?B?AAAA?=")]
    public void Decodes_encoded_words(string input, string expected) =>
        Assert.Equal(expected, ImapClient.DecodeWords(input));

    [Theory]
    [InlineData("\"Veli, Ali\" <ali@example.com>", "Veli, Ali")]
    [InlineData("Ali <ali@example.com>", "Ali")]
    [InlineData("<ali@example.com>", "ali@example.com")]
    [InlineData("ali@example.com", "ali@example.com")]
    public void Shows_the_sender_name_when_there_is_one(string from, string expected) =>
        Assert.Equal(expected, ImapClient.DisplayName(from));

    [Theory]
    [InlineData("kisi@gmail.com", "imap.gmail.com")]
    [InlineData("KISI@GoogleMail.com", "imap.gmail.com")]
    [InlineData("kisi@yahoo.com.tr", "imap.mail.yahoo.com")]
    [InlineData("kisi@yandex.com.tr", "imap.yandex.com")]
    [InlineData("kisi@icloud.com", "imap.mail.me.com")]
    public void Knows_common_providers(string address, string host)
    {
        Assert.Equal(host, MailProviders.Find(address)?.Host);
        Assert.False(MailProviders.IsMicrosoft(address));
    }

    [Theory]
    [InlineData("kisi@outlook.com")]
    [InlineData("kisi@Hotmail.com")]
    [InlineData("kisi@live.com")]
    [InlineData("kisi@outlook.com.tr")]
    public void Flags_microsoft_consumer_addresses(string address)
    {
        Assert.True(MailProviders.IsMicrosoft(address));
        Assert.Null(MailProviders.Find(address));
    }

    [Fact]
    public void Unknown_domains_have_no_preset() => Assert.Null(MailProviders.Find("kisi@sirket.example"));

    // ---- /mail eklentisi ----

    private static (MailPlugin Plugin, NullHost Host, List<string> Opened) Make(Func<MailAccount?> account, ImapConnect connect, Action? settings = null)
    {
        var host = new NullHost();
        var opened = new List<string>();
        var plugin = new MailPlugin(account, connect, opened.Add, settings ?? (() => { }), () => new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(3)).LocalDateTime);
        plugin.Init(new PluginContext(MailPlugin.Metadata, "", host));
        return (plugin, host, opened);
    }

    private static Task<IReadOnlyList<Result>> Ask(MailPlugin plugin, string search = "") =>
        plugin.QueryAsync(new Query($"/mail {search}".Trim(), "/mail", search), CancellationToken.None);

    [Fact]
    public async Task Plugin_without_account_opens_settings()
    {
        var opened = 0;
        var (plugin, _, _) = Make(() => null, (_, _, _) => throw new InvalidOperationException(), () => opened++);

        var row = Assert.Single(await Ask(plugin));

        Assert.Equal("Mail hesabı bağlı değil", row.Title);
        Assert.True(row.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Plugin_lists_unread_filters_and_opens_the_webmail()
    {
        var gmail = new MailAccount("imap.gmail.com", 993, "mami@gmail.com", "x");
        using var server = new FakeImapServer(gmail.Address, gmail.Password, [11, 12, 13], Headers);
        var (plugin, host, opened) = Make(() => gmail, server.Connect);

        Assert.Equal("Mailler yükleniyor…", Assert.Single(await Ask(plugin)).Title);
        await Wait.Until(() => host.Refreshes.Contains("/mail"));

        var rows = await Ask(plugin);
        Assert.Equal(["(konu yok)", "Günün ınsanları haberler", "Toplantı notları"], rows.Select(r => r.Title));
        Assert.Equal("Özlem Ağac · 2 saat önce · Enter ile gelen kutusunu aç", rows[1].Subtitle);

        var filtered = await Ask(plugin, "veli");
        Assert.Equal("Toplantı notları", Assert.Single(filtered).Title);
        filtered[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://mail.google.com/mail/u/0/#inbox"], opened);

        Assert.Equal("Eşleşen mail yok", Assert.Single(await Ask(plugin, "zzzzz")).Title);
    }

    [Fact]
    public async Task Plugin_shows_the_total_when_more_mail_is_unread_than_listed()
    {
        var headers = Enumerable.Range(1, 25).ToDictionary(i => i, i => $"From: a{i}@example.com\r\nSubject: Mail {i}\r\n\r\n");
        using var server = new FakeImapServer(Account.Address, Account.Password, [.. headers.Keys], headers);
        var (plugin, host, _) = Make(() => Account, server.Connect);
        await Ask(plugin);
        await Wait.Until(() => host.Refreshes.Contains("/mail"));

        var rows = await Ask(plugin);

        Assert.Equal(21, rows.Count);
        Assert.Equal("Mail 25", rows[0].Title);
        Assert.Equal("Toplam 25 okunmamış mail", rows[^1].Title);
    }

    [Fact]
    public async Task Plugin_reports_login_errors_and_opens_settings()
    {
        var opened = 0;
        using var server = new FakeImapServer(Account.Address, "başka", [11], Headers);
        var (plugin, host, _) = Make(() => Account, server.Connect, () => opened++);
        await Ask(plugin);
        await Wait.Until(() => host.Refreshes.Contains("/mail"));

        var row = Assert.Single(await Ask(plugin));

        Assert.StartsWith("Mailler alınamadı — giriş reddedildi", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Plugin_without_known_webmail_copies_the_subject()
    {
        var custom = new MailAccount("imap.sirket.example", 993, "mami@sirket.example", "x");
        using var server = new FakeImapServer(custom.Address, custom.Password, [11], Headers);
        var (plugin, host, opened) = Make(() => custom, server.Connect);
        await Ask(plugin);
        await Wait.Until(() => host.Refreshes.Contains("/mail"));

        var row = Assert.Single(await Ask(plugin));
        row.Action(new ActionContext(ActionModifiers.None));

        Assert.Empty(opened);
        Assert.Equal(["Toplantı notları"], host.Copied);
    }
}

public class AccountTests
{
    [Fact]
    public void Calendar_urls_are_validated_and_deduplicated()
    {
        var urls = AccountInput.ParseCalendarUrls(
            "# bir yorum\r\nhttps://calendar.google.com/a.ics\n\n  webcal://outlook.live.com/b.ics  \nhttps://calendar.google.com/a.ics\nmerhaba\nftp://x/y.ics",
            out var errors);

        Assert.Equal(["https://calendar.google.com/a.ics", "webcal://outlook.live.com/b.ics"], urls);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("6. satır", errors[0]);
        Assert.StartsWith("7. satır", errors[1]);
    }

    [Fact]
    public void Gmail_needs_only_address_and_password()
    {
        var (account, error) = AccountInput.BuildMail(" mami@gmail.com ", "abcd efgh", "", null);

        Assert.Null(error);
        Assert.Equal(new MailAccount("imap.gmail.com", 993, "mami@gmail.com", "abcd efgh"), account);
    }

    [Fact]
    public void Empty_address_removes_the_account() => Assert.Equal((null, null), AccountInput.BuildMail("  ", "x", "", null));

    [Fact]
    public void Empty_password_keeps_the_saved_one_for_the_same_address_only()
    {
        var saved = new MailAccount("imap.gmail.com", 993, "mami@gmail.com", "gizli");

        Assert.Equal("gizli", AccountInput.BuildMail("MAMI@gmail.com", "", "", saved).Account?.Password);
        Assert.NotNull(AccountInput.BuildMail("baska@gmail.com", "", "", saved).Error);
    }

    [Theory]
    [InlineData("mami@outlook.com", "Microsoft")]
    [InlineData("mami@hotmail.com", "Microsoft")]
    [InlineData("mami-yok", "geçerli")]
    [InlineData("mami@sirket.example", "sunucusunu")]
    public void Rejects_what_cannot_work(string address, string errorPart)
    {
        var (account, error) = AccountInput.BuildMail(address, "x", "", null);

        Assert.Null(account);
        Assert.Contains(errorPart, error);
    }

    [Theory]
    [InlineData("imap.sirket.example", "imap.sirket.example", 993)]
    [InlineData("imap.sirket.example:143", "imap.sirket.example", 143)]
    public void Custom_servers_with_optional_port(string server, string host, int port)
    {
        var (account, error) = AccountInput.BuildMail("mami@sirket.example", "x", server, null);

        Assert.Null(error);
        Assert.Equal(new MailAccount(host, port, "mami@sirket.example", "x"), account);
        Assert.Equal(server, AccountInput.ServerText(account!));
    }

    [Fact]
    public void Bad_ports_are_rejected() => Assert.NotNull(AccountInput.BuildMail("mami@sirket.example", "x", "imap.sirket.example:abc", null).Error);

    [Fact]
    public void Preset_servers_are_hidden_in_the_settings_box() =>
        Assert.Equal("", AccountInput.ServerText(new MailAccount("imap.gmail.com", 993, "mami@gmail.com", "x")));

    // ---- SecretStore ----

    private static byte[] Scramble(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "lumorago-test-" + Guid.NewGuid().ToString("N"), "accounts.dat");

    [Fact]
    public void Secrets_round_trip_and_are_not_stored_in_clear()
    {
        var path = TempFile();
        try
        {
            var secrets = new AccountSecrets
            {
                CalendarUrls = ["https://calendar.example.com/gizli-adres.ics"],
                Mail = new MailAccount("imap.gmail.com", 993, "mami@gmail.com", "cok-gizli-sifre"),
            };
            var changed = 0;
            var store = new SecretStore(path, Scramble, Scramble);
            store.Changed += (_, _) => changed++;

            store.Save(secrets);

            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("cok-gizli-sifre", raw);
            Assert.DoesNotContain("gizli-adres", raw);
            Assert.Equal(1, changed);

            var reopened = new SecretStore(path, Scramble, Scramble);
            Assert.Equal(secrets.CalendarUrls, reopened.Current.CalendarUrls);
            Assert.Equal(secrets.Mail, reopened.Current.Mail);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Unreadable_secrets_open_empty_and_are_logged()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var log = new ListLog();

            var store = new SecretStore(path, Scramble, _ => throw new System.Security.Cryptography.CryptographicException("başka kullanıcı"), log);

            Assert.Empty(store.Current.CalendarUrls);
            Assert.Null(store.Current.Mail);
            Assert.Single(log.Entries);

            File.WriteAllBytes(path, Scramble("bu json degil"u8.ToArray()));
            Assert.Null(new SecretStore(path, Scramble, Scramble).Current.Mail);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Missing_file_opens_empty()
    {
        var store = new SecretStore(TempFile(), Scramble, Scramble);
        Assert.Empty(store.Current.CalendarUrls);
        Assert.Null(store.Current.Mail);
    }
}
