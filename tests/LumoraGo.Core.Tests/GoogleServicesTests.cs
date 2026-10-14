using System.Net;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Calendar;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Google;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class GoogleServicesTests : IDisposable
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-gsvc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static byte[] Flip(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();

    private SecretStore Secrets(bool connected = true)
    {
        var secrets = new SecretStore(Path.Combine(_dir, (connected ? "on" : "off") + "-accounts.dat"), Flip, Flip);
        if (connected)
        {
            secrets.Save(secrets.Current with { Google = new GoogleAccount("id", "secret", "refresh", "me@gmail.com") });
        }

        return secrets;
    }

    /// <summary>Token isteğini cevaplar, diğer istekleri <paramref name="api"/>'ye bırakır ve gövdeleriyle kaydeder.</summary>
    private static (GoogleApi Api, List<(HttpMethod Method, string Url, string? Body)> Calls, Func<int> TokenCalls) Api(
        SecretStore secrets, Func<HttpRequestMessage, HttpResponseMessage> api)
    {
        var calls = new List<(HttpMethod, string, string?)>();
        var tokens = 0;
        var handler = new FakeHandler(r =>
        {
            if (r.RequestUri!.Host == "oauth2.googleapis.com")
            {
                tokens++;
                return FakeHandler.Json("""{"access_token":"fresh"}""");
            }

            Assert.Equal("fresh", r.Headers.Authorization?.Parameter);
            calls.Add((r.Method, r.RequestUri.ToString(), r.Content?.ReadAsStringAsync().Result));
            return api(r);
        });
        return (new GoogleApi(new HttpClient(handler), secrets), calls, () => tokens);
    }

    [Fact]
    public void Errors_say_which_api_to_enable_or_to_reconnect()
    {
        Assert.Contains("Google Tasks API açık değil", GoogleApi.Error(GoogleService.Tasks, HttpStatusCode.Forbidden, """{"error":{"details":[{"reason":"SERVICE_DISABLED"}]}}"""));
        Assert.Contains("YouTube izni yok", GoogleApi.Error(GoogleService.YouTube, HttpStatusCode.Forbidden, """{"error":{"details":[{"reason":"ACCESS_TOKEN_SCOPE_INSUFFICIENT"}]}}"""));
        Assert.Contains("Google Drive okunamadı (HTTP 500: boom)", GoogleApi.Error(GoogleService.Drive, HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}"""));
    }

    [Fact]
    public void Sign_in_asks_for_every_service()
    {
        var url = GoogleContacts.AuthUrl("id", "http://127.0.0.1:1", "c", "s");
        foreach (var scope in new[] { GoogleCalendar.Scope, GoogleTasks.Scope, GoogleDrive.Scope, YouTubeLibrary.Scope })
        {
            Assert.Contains(Uri.EscapeDataString(scope), url);
        }
    }

    [Fact]
    public async Task Access_token_is_reused_and_not_connected_is_explained()
    {
        var (api, _, tokens) = Api(Secrets(), _ => FakeHandler.Json("""{"items":[]}"""));
        var calendar = new GoogleCalendar(api, Istanbul);
        await calendar.GetAsync(new DateTime(2026, 10, 14), new DateTime(2026, 10, 15), null, CancellationToken.None);
        await calendar.GetAsync(new DateTime(2026, 10, 14), new DateTime(2026, 10, 15), null, CancellationToken.None);
        Assert.Equal(1, tokens());

        var (offline, _, _) = Api(Secrets(connected: false), _ => FakeHandler.Json("{}"));
        Assert.False(new GoogleCalendar(offline).IsConfigured);
        var ex = await Assert.ThrowsAsync<GoogleException>(() => new GoogleTasks(offline).GetAsync(CancellationToken.None));
        Assert.Equal(GoogleApi.NotConnected, ex.Message);
    }

    [Fact]
    public async Task Calendar_events_are_read_in_local_time_with_meeting_links()
    {
        const string json = """
            {"items":[
              {"summary":"Toplantı","start":{"dateTime":"2026-10-14T07:00:00Z"},"end":{"dateTime":"2026-10-14T08:00:00Z"},"hangoutLink":"https://meet.google.com/abc-defg-hij"},
              {"summary":"Doğum günü","start":{"date":"2026-10-15"},"end":{"date":"2026-10-16"}},
              {"summary":"İptal","status":"cancelled","start":{"dateTime":"2026-10-14T09:00:00Z"}},
              {"summary":"Zoom","start":{"dateTime":"2026-10-14T13:00:00+03:00"},"end":{"dateTime":"2026-10-14T14:00:00+03:00"},"description":"Katıl: https://us02web.zoom.us/j/123"}
            ]}
            """;
        var (api, calls, _) = Api(Secrets(), _ => FakeHandler.Json(json));
        var items = await new GoogleCalendar(api, Istanbul).GetAsync(new DateTime(2026, 10, 14), new DateTime(2026, 10, 21), null, CancellationToken.None);

        Assert.Equal(["Toplantı", "Doğum günü", "Zoom"], items.Select(i => i.Title));
        Assert.Equal(new DateTime(2026, 10, 14, 10, 0, 0), items[0].Start);
        Assert.Equal("https://meet.google.com/abc-defg-hij", items[0].MeetingUrl);
        Assert.True(items[1].AllDay);
        Assert.Equal(new DateTime(2026, 10, 15), items[1].Start);
        Assert.Equal("https://us02web.zoom.us/j/123", items[2].MeetingUrl);
        Assert.Contains("singleEvents=true", calls[0].Url);
        Assert.Contains("calendars/primary/events", calls[0].Url);
    }

    [Fact]
    public async Task Calendar_error_is_reported_not_thrown()
    {
        var (api, _, _) = Api(Secrets(), _ => FakeHandler.Json("""{"error":{"details":[{"reason":"SERVICE_DISABLED"}]}}""", HttpStatusCode.Forbidden));
        var errors = new List<string>();
        var items = await new GoogleCalendar(api).GetAsync(DateTime.Today, DateTime.Today.AddDays(1), errors.Add, CancellationToken.None);
        Assert.Empty(items);
        Assert.Contains("Google Calendar API", Assert.Single(errors));
    }

    [Fact]
    public async Task Adding_an_event_sends_local_time_or_all_day()
    {
        var (api, calls, _) = Api(Secrets(), _ => FakeHandler.Json("{}"));
        var calendar = new GoogleCalendar(api, Istanbul);
        await calendar.AddAsync("Dişçi", new DateTime(2026, 10, 16, 14, 0, 0), true, CancellationToken.None);
        await calendar.AddAsync("Bayram", new DateTime(2026, 10, 29), false, CancellationToken.None);

        Assert.All(calls, c => Assert.Equal(HttpMethod.Post, c.Method));
        using var timed = JsonDocument.Parse(calls[0].Body!);
        Assert.Equal("Dişçi", timed.RootElement.GetProperty("summary").GetString());
        Assert.Equal("2026-10-16T14:00:00+03:00", timed.RootElement.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("2026-10-16T15:00:00+03:00", timed.RootElement.GetProperty("end").GetProperty("dateTime").GetString());
        using var allDay = JsonDocument.Parse(calls[1].Body!);
        Assert.Equal("2026-10-29", allDay.RootElement.GetProperty("start").GetProperty("date").GetString());
        Assert.Equal("2026-10-30", allDay.RootElement.GetProperty("end").GetProperty("date").GetString());
    }

    [Fact]
    public async Task Combined_calendar_merges_sources_and_drops_duplicates()
    {
        var a = new FixedSource(new CalendarItem("A", new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), false, null, null));
        var b = new FixedSource(
            new CalendarItem("A", new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), false, null, null),
            new CalendarItem("B", new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 9, 30, 0), false, null, null));
        var off = new FixedSource { IsConfigured = false };
        var combined = new CombinedCalendarSource(a, b, off);
        Assert.True(combined.IsConfigured);
        Assert.Equal(["B", "A"], (await combined.GetAsync(default, default, null, CancellationToken.None)).Select(i => i.Title));
        Assert.False(new CombinedCalendarSource(off).IsConfigured);
    }

    private sealed class FixedSource(params CalendarItem[] items) : ICalendarSource
    {
        public bool IsConfigured { get; init; } = true;

        public Task<IReadOnlyList<CalendarItem>> GetAsync(DateTime fromLocal, DateTime toLocal, Action<string>? errors, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CalendarItem>>(items);
    }

    [Fact]
    public async Task Tasks_are_read_from_every_list_added_and_completed()
    {
        var (api, calls, _) = Api(Secrets(), r =>
        {
            var url = r.RequestUri!.ToString();
            return url.Contains("users/@me/lists")
                ? FakeHandler.Json("""{"items":[{"id":"L1","title":"Görevlerim"},{"id":"L2","title":"Market"}]}""")
                : url.Contains("lists/L1/tasks?")
                    ? FakeHandler.Json("""{"items":[{"id":"t1","title":"Fatura öde","due":"2026-10-15T00:00:00.000Z","notes":"elektrik"},{"id":"t0","title":"Bitti","status":"completed"},{"id":"t9","title":""}]}""")
                    : url.Contains("lists/L2/tasks?")
                        ? FakeHandler.Json("""{"items":[{"id":"t2","title":"Süt"}]}""")
                        : FakeHandler.Json("{}");
        });
        var tasks = new GoogleTasks(api);
        var items = await tasks.GetAsync(CancellationToken.None);
        Assert.Equal(["Fatura öde", "Süt"], items.Select(t => t.Title));
        Assert.Equal(new DateTime(2026, 10, 15), items[0].Due);
        Assert.Equal("Market", items[1].ListTitle);

        await tasks.AddAsync("Kira", new DateTime(2026, 11, 1), CancellationToken.None);
        await tasks.CompleteAsync(items[1], CancellationToken.None);
        var add = calls[^2];
        Assert.Equal(HttpMethod.Post, add.Method);
        Assert.EndsWith("lists/@default/tasks", add.Url);
        Assert.Contains("\"due\":\"2026-11-01T00:00:00.000Z\"", add.Body);
        var complete = calls[^1];
        Assert.Equal(HttpMethod.Patch, complete.Method);
        Assert.EndsWith("lists/L2/tasks/t2", complete.Url);
        Assert.Contains("\"status\":\"completed\"", complete.Body);
    }

    [Theory]
    [InlineData(-2, "Gecikti (12 Ekim)")]
    [InlineData(0, "Bugün")]
    [InlineData(1, "Yarın")]
    [InlineData(20, "3 Kasım")]
    public void Task_due_dates_read_naturally(int days, string expected) =>
        Assert.Equal(expected, TasksPlugin.Due(new DateTime(2026, 10, 14).AddDays(days), new DateTime(2026, 10, 14)));

    [Fact]
    public async Task Tasks_plugin_offers_add_row_and_lists_after_loading()
    {
        var (api, _, _) = Api(Secrets(), r => r.RequestUri!.ToString().Contains("users/@me/lists")
            ? FakeHandler.Json("""{"items":[{"id":"L1","title":"Görevlerim"}]}""")
            : FakeHandler.Json("""{"items":[{"id":"t1","title":"Fatura öde","due":"2026-10-15T00:00:00.000Z"}]}"""));
        var refreshed = new TaskCompletionSource();
        var plugin = new TasksPlugin(new GoogleTasks(api), _ => { }, now: () => new DateTime(2026, 10, 14, 12, 0, 0));
        plugin.Init(new PluginContext(plugin.Metadata, _dir, new RefreshHost(refreshed)));

        var first = await plugin.QueryAsync(new Query("/görev", "/görev", ""), CancellationToken.None);
        Assert.Equal("Görevler yükleniyor…", Assert.Single(first).Title);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var listed = await plugin.QueryAsync(new Query("/görev", "/görev", ""), CancellationToken.None);
        Assert.Equal("Fatura öde", Assert.Single(listed).Title);
        Assert.StartsWith("Yarın", listed[0].Subtitle);

        var typed = await plugin.QueryAsync(new Query("/görev yarın süt al", "/görev", "yarın süt al"), CancellationToken.None);
        Assert.Equal("Görev ekle: süt al", typed[0].Title);
        Assert.StartsWith("Yarın", typed[0].Subtitle);
    }

    private sealed class RefreshHost(TaskCompletionSource refreshed) : IPluginHost
    {
        public void ChangeQuery(string text) { }
        public void HideLauncher() { }
        public void CopyToClipboard(string text) { }
        public void RefreshResults(string keyword) => refreshed.TrySetResult();
        public void ShowMessage(string title, string message) { }
        public void Log(LogLevel level, string message) { }
    }

    [Fact]
    public async Task Tasks_plugin_without_google_points_to_settings()
    {
        var (api, _, _) = Api(Secrets(connected: false), _ => FakeHandler.Json("{}"));
        var opened = false;
        var plugin = new TasksPlugin(new GoogleTasks(api), _ => { }, () => opened = true);
        var row = Assert.Single(await plugin.QueryAsync(new Query("/görev", "/görev", ""), CancellationToken.None));
        Assert.Equal("Google Görevler bağlı değil", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.True(opened);
    }

    [Fact]
    public async Task Drive_search_escapes_quotes_and_names_file_kinds()
    {
        var (api, calls, _) = Api(Secrets(), _ => FakeHandler.Json("""
            {"files":[
              {"name":"Bütçe 2026","mimeType":"application/vnd.google-apps.spreadsheet","webViewLink":"https://docs.google.com/spreadsheets/d/1","modifiedTime":"2026-10-01T10:00:00Z"},
              {"name":"foto.jpg","mimeType":"image/jpeg","webViewLink":"https://drive.google.com/file/d/2"},
              {"name":"linksiz"}
            ]}
            """));
        var files = await new GoogleDrive(api).SearchAsync("ali'nin", CancellationToken.None);
        Assert.Equal(["Bütçe 2026", "foto.jpg"], files.Select(f => f.Name));
        Assert.Equal(["Tablo", "Resim"], files.Select(f => f.Kind));
        var q = Uri.UnescapeDataString(calls[0].Url);
        Assert.Contains("name contains 'ali\\'nin'", q);
        Assert.Contains("trashed = false", q);

        await new GoogleDrive(api).SearchAsync("", CancellationToken.None);
        Assert.Contains("viewedByMeTime desc", Uri.UnescapeDataString(calls[1].Url));
        Assert.DoesNotContain("name contains", Uri.UnescapeDataString(calls[1].Url));
    }

    [Fact]
    public async Task YouTube_library_collects_subscriptions_playlists_and_likes_and_survives_restart()
    {
        var (api, calls, _) = Api(Secrets(), r =>
        {
            var url = r.RequestUri!.ToString();
            if (url.Contains("subscriptions?"))
            {
                return url.Contains("pageToken=s2")
                    ? FakeHandler.Json("""{"items":[{"snippet":{"title":"Barış Özcan","resourceId":{"channelId":"UC2"}}}]}""")
                    : FakeHandler.Json("""{"items":[{"snippet":{"title":"Ruhi Çenet","resourceId":{"channelId":"UC1"}}}],"nextPageToken":"s2"}""");
            }

            if (url.Contains("playlists?"))
            {
                return FakeHandler.Json("""{"items":[{"id":"PL1","snippet":{"title":"Spor müzikleri"},"contentDetails":{"itemCount":42}}]}""");
            }

            if (url.Contains("channels?"))
            {
                return FakeHandler.Json("""{"items":[{"contentDetails":{"relatedPlaylists":{"likes":"LL"}}}]}""");
            }

            return FakeHandler.Json("""
                {"items":[
                  {"snippet":{"title":"Gol anları","videoOwnerChannelTitle":"beIN","resourceId":{"videoId":"v1"}}},
                  {"snippet":{"title":"Deleted video","resourceId":{"videoId":"v2"}}}
                ]}
                """);
        });
        var cache = Path.Combine(_dir, "youtube.json");
        var library = new YouTubeLibrary(api, cache);
        Assert.Equal(4, await library.RefreshAsync(CancellationToken.None));
        Assert.Contains(calls, c => c.Url.Contains("playlistItems?") && c.Url.Contains("playlistId=LL"));

        var reopened = new YouTubeLibrary(api, cache);
        Assert.Equal(["Ruhi Çenet", "Barış Özcan", "Spor müzikleri", "Gol anları"], reopened.Items.Select(i => i.Title));
        Assert.Equal("https://www.youtube.com/channel/UC1", reopened.Items[0].Url);
        Assert.Equal("Oynatma listen · 42 video", reopened.Items[2].Detail);
        Assert.Equal("https://www.youtube.com/watch?v=v1", reopened.Items[3].Url);
        Assert.Equal("Beğendiğin video · beIN", reopened.Items[3].Detail);

        var plugin = new YouTubePlugin(reopened, _ => { });
        var results = await plugin.QueryAsync(new Query("/youtube baris", "/youtube", "baris"), CancellationToken.None);
        Assert.Equal(["Barış Özcan", "YouTube'da ara: baris"], results.Select(r => r.Title));
    }

    [Fact]
    public async Task Direct_actions_add_events_and_tasks_through_google_when_connected()
    {
        var (api, calls, _) = Api(Secrets(), _ => FakeHandler.Json("{}"));
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        var plugin = new DirectActionsPlugin(
            new LumoraGo.Core.Actions.ReminderStore(Path.Combine(_dir, "r.json")),
            new ContactBook(Path.Combine(_dir, "c.dat"), Flip, Flip),
            new LumoraGo.Core.Notes.NotesStore(Path.Combine(_dir, "n.json")),
            _ => Assert.Fail("tarayıcı açılmamalı"),
            () => now,
            new GoogleCalendar(api, Istanbul),
            new GoogleTasks(api));

        var ev = Assert.Single(await plugin.QueryAsync(new Query("etkinlik yarın 15:00 dişçi", null, "etkinlik yarın 15:00 dişçi"), CancellationToken.None));
        Assert.Contains("Google Takvim'e eklenir", ev.Subtitle);
        ev.Action(new ActionContext(ActionModifiers.None));

        var task = Assert.Single(await plugin.QueryAsync(new Query("görev süt al", null, "görev süt al"), CancellationToken.None));
        Assert.Equal("Görev: süt al", task.Title);
        task.Action(new ActionContext(ActionModifiers.None));

        for (var i = 0; i < 50 && calls.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        string Field(string url, string name) =>
            JsonDocument.Parse(calls.Single(c => c.Url.Contains(url)).Body!).RootElement.GetProperty(name).GetString()!;
        Assert.Equal("dişçi", Field("calendars/primary/events", "summary"));
        Assert.Equal("süt al", Field("lists/@default/tasks", "title"));
    }
}
