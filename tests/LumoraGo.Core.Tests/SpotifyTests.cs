using System.Net;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Desktop;
using LumoraGo.Core.Plugins;
using LumoraGo.Core.Spotify;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class SpotifyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spotify-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static byte[] Flip(byte[] data) => data;

    private SecretStore Secrets(bool connected)
    {
        var secrets = new SecretStore(Path.Combine(_dir, (connected ? "on" : "off") + ".dat"), Flip, Flip);
        if (connected)
        {
            secrets.Save(secrets.Current with { Spotify = new SpotifyAccount("cid", "refresh", "mami", true) });
        }

        return secrets;
    }

    private const string Search = """
        {"tracks":{"items":[{"name":"Şımarık","uri":"spotify:track:1","artists":[{"name":"Tarkan"}],"album":{"name":"Ölürüm Sana"},"external_urls":{"spotify":"https://open.spotify.com/track/1"}}]},
         "playlists":{"items":[null,{"name":"Tarkan Hits","uri":"spotify:playlist:2","owner":{"display_name":"Spotify"},"tracks":{"total":50}}]},
         "albums":{"items":[{"name":"Karma","uri":"spotify:album:3","release_date":"2001-07-30","artists":[{"name":"Tarkan"}]}]},
         "artists":{"items":[{"name":"Tarkan","uri":"spotify:artist:4"}]}}
        """;

    [Fact]
    public void ParseSearch_ReadsAllKindsAndSkipsNulls()
    {
        using var doc = JsonDocument.Parse(Search);
        var items = SpotifyApi.ParseSearch(doc.RootElement);
        Assert.Equal([SpotifyKind.Track, SpotifyKind.Playlist, SpotifyKind.Album, SpotifyKind.Artist], items.Select(i => i.Kind));
        Assert.Equal("Tarkan · Ölürüm Sana", items[0].Detail);
        Assert.Equal("Spotify · 50 şarkı", items[1].Detail);
        Assert.Equal("Tarkan · 2001", items[2].Detail);
    }

    [Fact]
    public void ParseNowPlaying_HandlesEmptyPlayer()
    {
        using var empty = JsonDocument.Parse("{}");
        Assert.Null(SpotifyApi.ParseNowPlaying(empty.RootElement));
        using var doc = JsonDocument.Parse("""{"is_playing":true,"progress_ms":61000,"device":{"name":"PC"},"item":{"name":"Kuzu Kuzu","duration_ms":200000,"artists":[{"name":"Tarkan"}]}}""");
        Assert.Equal(new NowPlaying("Kuzu Kuzu", "Tarkan", true, "PC", 61000, 200000), SpotifyApi.ParseNowPlaying(doc.RootElement));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"status":404,"message":"Player command failed: No active device found","reason":"NO_ACTIVE_DEVICE"}}""", "cihazı yok")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"status":403,"message":"Premium required","reason":"PREMIUM_REQUIRED"}}""", "Premium")]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "yeniden bağlan")]
    public void Error_ExplainsCommonFailures(HttpStatusCode status, string body, string expected) =>
        Assert.Contains(expected, SpotifyApi.Error(status, body));

    [Theory]
    [InlineData("spotify:track:1", "https://open.spotify.com/track/1")]
    [InlineData("spotify:search:tarkan%20kuzu", "https://open.spotify.com/search/tarkan%20kuzu")]
    public void WebUrl_ConvertsUris(string uri, string url) => Assert.Equal(url, SpotifyApi.WebUrl(uri));

    [Fact]
    public async Task NotConnected_SearchesInAppAndControlsWithMediaKeys()
    {
        var media = new Media();
        var opened = new List<string>();
        var plugin = new SpotifyPlugin(new SpotifyApi(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), Secrets(false)),
            media, opened.Add, () => opened.Add("settings"));
        var results = await plugin.QueryAsync(new Query("/spotify tarkan kuzu", "/spotify", "tarkan kuzu"), CancellationToken.None);
        results[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal("spotify:search:tarkan%20kuzu", opened[0]);
        Assert.Equal("Spotify'ı bağla", results[^1].Title);

        var next = await plugin.QueryAsync(new Query("/spotify sonraki", "/spotify", "sonraki"), CancellationToken.None);
        next.OrderByDescending(r => r.Score).First().Action(new ActionContext(ActionModifiers.None));
        Assert.Equal([MediaKey.Next], media.Sent);
    }

    [Fact]
    public async Task Connected_SearchesAndPlaysOnDevice()
    {
        var calls = new List<(HttpMethod Method, string Url, string? Body)>();
        var handler = new FakeHandler(r =>
        {
            calls.Add((r.Method, r.RequestUri!.ToString(), r.Content?.ReadAsStringAsync().Result));
            return r.RequestUri!.Host == "accounts.spotify.com" ? FakeHandler.Json("""{"access_token":"tok","refresh_token":"new-refresh"}""")
                : r.RequestUri.AbsolutePath.EndsWith("/search") ? FakeHandler.Json(Search)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var secrets = Secrets(true);
        var plugin = new SpotifyPlugin(new SpotifyApi(new HttpClient(handler), secrets), new Media(), _ => { }, () => { });
        var refreshed = new TaskCompletionSource();
        plugin.Init(new PluginContext(plugin.Metadata, "", new Host(refreshed)));

        var query = new Query("/spotify tarkan", "/spotify", "tarkan");
        await plugin.QueryAsync(query, CancellationToken.None);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var results = await plugin.QueryAsync(query, CancellationToken.None);
        Assert.Equal("Şımarık", results[0].Title);
        Assert.Equal("new-refresh", secrets.Current.Spotify!.RefreshToken);

        results[1].Action(new ActionContext(ActionModifiers.None));
        for (var i = 0; i < 50 && !calls.Any(c => c.Method == HttpMethod.Put); i++)
        {
            await Task.Delay(20);
        }

        var play = Assert.Single(calls, c => c.Method == HttpMethod.Put);
        Assert.EndsWith("/me/player/play", play.Url);
        Assert.Equal("""{"context_uri":"spotify:playlist:2"}""", play.Body);
    }

    private sealed class Media : IMediaControl
    {
        public List<MediaKey> Sent { get; } = [];

        public int? GetVolume() => 50;

        public bool SetVolume(int percent) => true;

        public void Send(MediaKey key) => Sent.Add(key);
    }

    private sealed class Host(TaskCompletionSource refreshed) : IPluginHost
    {
        public void ChangeQuery(string text) { }
        public void HideLauncher() { }
        public void CopyToClipboard(string text) { }
        public void RefreshResults(string keyword) => refreshed.TrySetResult();
        public void ShowMessage(string title, string message) { }
        public void Log(LogLevel level, string message) { }
    }
}
