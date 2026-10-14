using System.Net;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Photos;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class GooglePhotosTests : IDisposable
{
    private const string Items = """
        {"mediaItems":[
          {"id":"AB-1","createTime":"2024-03-05T10:20:30Z","type":"PHOTO",
           "mediaFile":{"baseUrl":"https://lh3.googleusercontent.com/p1","mimeType":"image/heic","filename":"IMG_0001.HEIC",
             "mediaFileMetadata":{"width":4032,"height":3024,"photoMetadata":{}}}},
          {"id":"CD-2","createTime":"2023-07-01T08:00:00Z","type":"VIDEO",
           "mediaFile":{"baseUrl":"https://lh3.googleusercontent.com/v2","mimeType":"video/mp4","filename":"tatil.mp4"}},
          {"id":"bozuk"}
        ]}
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-photos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static byte[] Flip(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();

    private PhotoLibrary Library(string? local = null) => new(Path.Combine(_dir, "google"), () => local);

    [Fact]
    public void Session_and_items_are_parsed()
    {
        var session = GooglePhotosPicker.ParseSession("""
            {"id":"s1","pickerUri":"https://photos.google.com/picker/s1","pollingConfig":{"pollInterval":"3.5s","timeoutIn":"1800s"},"mediaItemsSet":false}
            """);
        Assert.Equal("s1", session.Id);
        Assert.False(session.MediaItemsSet);
        Assert.Equal(TimeSpan.FromSeconds(3.5), session.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), session.TimeoutIn);

        var (items, next) = GooglePhotosPicker.ParseItems(Items);
        Assert.Null(next);
        Assert.Equal(["AB-1", "CD-2"], items.Select(i => i.Id));
        Assert.Equal(4032, items[0].Width);
        Assert.False(items[0].IsVideo);
        Assert.True(items[1].IsVideo);
        Assert.Equal(new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.Zero), items[0].Created);
    }

    [Theory]
    [InlineData("image/jpeg", "a.HEIC", ".jpg")]
    [InlineData("image/png", "a.jpg", ".png")]
    [InlineData(null, "a.PNG", ".png")]
    [InlineData(null, "tatil.mp4", ".jpg")]
    [InlineData("application/octet-stream", "", ".jpg")]
    public void Downloaded_file_gets_the_served_format(string? mediaType, string fileName, string expected) =>
        Assert.Equal(expected, GooglePhotosPicker.Extension(mediaType, fileName));

    [Fact]
    public void Disabled_api_and_missing_permission_are_explained()
    {
        Assert.Contains("Photos Picker API", GooglePhotosPicker.ApiError(HttpStatusCode.Forbidden, """{"error":{"details":[{"reason":"SERVICE_DISABLED"}]}}"""));
        Assert.Equal(GooglePhotosPicker.NoPhotosScope, GooglePhotosPicker.ApiError(HttpStatusCode.Forbidden, """{"error":{"details":[{"reason":"ACCESS_TOKEN_SCOPE_INSUFFICIENT"}]}}"""));
        Assert.Equal(GooglePhotosPicker.NoPhotosScope, GooglePhotosPicker.ApiError(HttpStatusCode.Unauthorized, "{}"));
        Assert.Contains("HTTP 500: boom", GooglePhotosPicker.ApiError(HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}"""));
    }

    [Fact]
    public void Sign_in_asks_for_photo_picker_permission()
    {
        var url = GoogleContacts.AuthUrl("id", "http://127.0.0.1:1", "c", "s");
        Assert.Contains(Uri.EscapeDataString(GooglePhotosPicker.Scope), url);
    }

    [Fact]
    public async Task Pick_opens_browser_waits_for_selection_downloads_and_deletes_session()
    {
        var secrets = new SecretStore(Path.Combine(_dir, "accounts.dat"), Flip, Flip);
        secrets.Save(secrets.Current with { Google = new GoogleAccount("id", "secret", "refresh", "me@gmail.com") });
        var polls = 0;
        var deleted = false;
        var handler = new FakeHandler(r =>
        {
            var url = r.RequestUri!.ToString();
            if (r.RequestUri.Host == "oauth2.googleapis.com")
            {
                return FakeHandler.Json("""{"access_token":"fresh"}""");
            }

            Assert.Equal("fresh", r.Headers.Authorization?.Parameter);
            if (r.Method == HttpMethod.Post && url.EndsWith("/v1/sessions"))
            {
                return FakeHandler.Json("""{"id":"s1","pickerUri":"https://photos.google.com/picker/s1","pollingConfig":{"pollInterval":"1s","timeoutIn":"60s"},"mediaItemsSet":false}""");
            }

            if (r.Method == HttpMethod.Delete)
            {
                deleted = true;
                return FakeHandler.Json("{}");
            }

            if (url.Contains("/v1/sessions/s1"))
            {
                return FakeHandler.Json($$"""{"id":"s1","pickerUri":"x","mediaItemsSet":{{(++polls >= 2 ? "true" : "false")}}}""");
            }

            if (url.Contains("/v1/mediaItems?") && url.Contains("sessionId=s1"))
            {
                return FakeHandler.Json(Items);
            }

            if (url.StartsWith("https://lh3.googleusercontent.com/") && url.EndsWith("=w2048-h2048"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
                response.Content.Headers.ContentType = new("image/jpeg");
                return response;
            }

            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        var library = Library();
        string? opened = null;
        var import = new GooglePhotoImport(new HttpClient(handler), secrets, library, u => opened = u) { Delay = (_, _) => Task.CompletedTask };

        Assert.Equal(2, await import.PickAsync(CancellationToken.None));

        Assert.Equal("https://photos.google.com/picker/s1/autoclose", opened);
        Assert.Equal(2, polls);
        Assert.True(deleted);
        Assert.Equal(["IMG_0001.HEIC", "tatil.mp4"], library.Google.OrderByDescending(p => p.Date).Select(p => p.Name));
        Assert.All(library.Google, p => Assert.Equal(".jpg", Path.GetExtension(p.Path)));
        Assert.All(library.Google, p => Assert.Equal([1, 2, 3], File.ReadAllBytes(p.Path)));

        // Yeniden açılınca liste diskten gelir; aynı fotoğraf yeniden seçilirse çoğalmaz.
        var reopened = Library();
        Assert.Equal(2, reopened.Google.Count);
        polls = 0;
        await new GooglePhotoImport(new HttpClient(handler), secrets, reopened, _ => { }) { Delay = (_, _) => Task.CompletedTask }.PickAsync(CancellationToken.None);
        Assert.Equal(2, reopened.Google.Count);
    }

    [Fact]
    public async Task Pick_without_google_connection_says_so()
    {
        var secrets = new SecretStore(Path.Combine(_dir, "accounts.dat"), Flip, Flip);
        var import = new GooglePhotoImport(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}"))), secrets, Library(), _ => { });
        var ex = await Assert.ThrowsAsync<GoogleException>(() => import.PickAsync(CancellationToken.None));
        Assert.Contains("Google ile bağlan", ex.Message);
    }

    [Fact]
    public async Task Plugin_lists_google_and_local_photos_newest_first_and_filters()
    {
        var local = Path.Combine(_dir, "Resimler");
        Directory.CreateDirectory(Path.Combine(local, "Tatil"));
        File.WriteAllBytes(Path.Combine(local, "Tatil", "deniz.jpg"), [1]);
        File.SetLastWriteTime(Path.Combine(local, "Tatil", "deniz.jpg"), new DateTime(2022, 8, 1));
        File.WriteAllBytes(Path.Combine(local, "notlar.txt"), [1]);
        var library = Library(local);
        library.AddGoogle("g1", "kahvalti.jpg", new DateTime(2024, 3, 5), ".jpg", [1]);
        library.ScanNow();

        var secrets = new SecretStore(Path.Combine(_dir, "accounts.dat"), Flip, Flip);
        var import = new GooglePhotoImport(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}"))), secrets, library, _ => { });
        var plugin = new PhotosPlugin(library, import, () => true, _ => { }, _ => { });
        plugin.Init(new PluginContext(plugin.Metadata, _dir, new NullHost()));

        var all = await plugin.QueryAsync(new Query("/foto", "/foto", ""), CancellationToken.None);
        Assert.Equal(["Google Fotoğraflar'dan fotoğraf seç", "kahvalti.jpg", "deniz.jpg"], all.Select(r => r.Title));
        Assert.Equal(library.Google[0].Path, all[1].PreviewPath);
        Assert.StartsWith(PhotosPlugin.ThumbnailPrefix, all[1].IconPath);
        Assert.Contains("Tatil", all[2].Subtitle);

        Assert.Equal(["deniz.jpg"], (await plugin.QueryAsync(new Query("/foto tatil", "/foto", "tatil"), CancellationToken.None)).Select(r => r.Title));
        Assert.Equal(["kahvalti.jpg"], (await plugin.QueryAsync(new Query("/foto mart", "/foto", "mart"), CancellationToken.None)).Select(r => r.Title));

        library.RemoveGoogle(library.Google[0]);
        Assert.Empty(library.Google);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "google"), "*.jpg"));
    }

    [Fact]
    public async Task Plugin_without_google_points_to_settings()
    {
        var secrets = new SecretStore(Path.Combine(_dir, "accounts.dat"), Flip, Flip);
        var library = Library();
        var opened = false;
        var plugin = new PhotosPlugin(library, new GooglePhotoImport(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}"))), secrets, library, _ => { }),
            () => false, _ => { }, _ => { }, () => opened = true);
        var first = (await plugin.QueryAsync(new Query("/foto", "/foto", ""), CancellationToken.None))[0];
        Assert.Equal("Google Fotoğraflar'ı bağla", first.Title);
        first.Action(new ActionContext(ActionModifiers.None));
        Assert.True(opened);
    }
}
