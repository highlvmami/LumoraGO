using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;

namespace LumoraGo.Core.Spotify;

public sealed class SpotifyException(string message) : Exception(message);

public enum SpotifyKind
{
    Track,
    Playlist,
    Album,
    Artist,
}

/// <param name="Uri">"spotify:track:..." gibi; uygulamada açmak ve çalmak için.</param>
public sealed record SpotifyItem(SpotifyKind Kind, string Name, string Detail, string Uri, string? Url);

public sealed record NowPlaying(string Track, string Artist, bool IsPlaying, string? Device, int ProgressMs, int DurationMs);

/// <summary>
/// Spotify Web API: kullanıcının kendi geliştirici uygulamasıyla (PKCE, gizli anahtar gerekmez) giriş, arama, çalma listeleri,
/// şu an çalan ve çalma. Çalmak için Spotify Premium ve açık bir Spotify cihazı gerekir.
/// </summary>
public sealed class SpotifyApi(HttpClient http, SecretStore secrets, Func<DateTime>? utcNow = null)
{
    /// <summary>Geliştirici panelinde Redirect URI olarak birebir bu yazılmalı.</summary>
    public const int RedirectPort = 8975;
    public static readonly string RedirectUri = $"http://127.0.0.1:{RedirectPort}/callback";

    private const string Scopes = "user-read-private user-read-playback-state user-modify-playback-state user-read-currently-playing playlist-read-private playlist-read-collaborative";
    private const string Api = "https://api.spotify.com/v1/";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);

    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private (string Access, DateTime At)? _token;

    /// <summary>"spotify:track:ID" → "https://open.spotify.com/track/ID"; aramalar "/search/..." olur.</summary>
    public static string WebUrl(string uri)
    {
        var parts = uri.Split(':', 3);
        return parts is ["spotify", var kind, var rest] ? $"https://open.spotify.com/{kind}/{rest}" : uri;
    }

    public bool IsConnected => secrets.Current.Spotify is { RefreshToken.Length: > 0 };

    public const string NotConnected = "Spotify bağlı değil. Ayarlar → Spotify → \"Spotify ile bağlan\".";

    public static async Task<SpotifyAccount> SignInAsync(HttpClient http, string clientId, Action<string> openBrowser, CancellationToken ct)
    {
        clientId = clientId.Trim();
        if (clientId.Length == 0)
        {
            throw new SpotifyException("Önce Client ID'yi yapıştır.");
        }

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, RedirectPort);
            listener.Start();
        }
        catch (SocketException)
        {
            throw new SpotifyException($"{RedirectPort} bağlantı noktası başka bir uygulamada açık; onu kapatıp yeniden dene.");
        }

        using (listener)
        {
            var (verifier, challenge) = GoogleContacts.NewPkce();
            var state = GoogleContacts.NewState();
            openBrowser("https://accounts.spotify.com/authorize?" + Form(new()
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = RedirectUri,
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = challenge,
                ["scope"] = Scopes,
                ["state"] = state,
            }));

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            string code;
            try
            {
                code = await GoogleContacts.ReceiveCodeAsync(listener, state, limit.Token, "Spotify",
                    "LumoraGo Spotify'a bağlandı. Bu sekmeyi kapatabilirsin.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpotifyException("Spotify girişi 5 dakika içinde tamamlanmadı.");
            }
            catch (GoogleException ex)
            {
                throw new SpotifyException(ex.Message);
            }

            var (access, refresh) = await TokenAsync(http, new()
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier,
            }, ct);
            if (refresh is not { Length: > 0 })
            {
                throw new SpotifyException("Spotify yenileme anahtarı vermedi; yeniden bağlan.");
            }

            using var me = await SendAsync(http, access, HttpMethod.Get, Api + "me", null, ct);
            var root = me.RootElement;
            var name = Str(root, "display_name") ?? Str(root, "id") ?? "";
            return new SpotifyAccount(clientId, refresh, name, Str(root, "product") == "premium");
        }
    }

    public async Task<IReadOnlyList<SpotifyItem>> SearchAsync(string text, CancellationToken ct)
    {
        using var doc = await GetAsync($"search?q={Uri.EscapeDataString(text)}&type=track,playlist,artist,album&limit=5", ct);
        return ParseSearch(doc.RootElement);
    }

    public async Task<IReadOnlyList<SpotifyItem>> PlaylistsAsync(CancellationToken ct)
    {
        using var doc = await GetAsync("me/playlists?limit=50", ct);
        return Items(doc.RootElement).Select(p => Playlist(p)).OfType<SpotifyItem>().ToList();
    }

    /// <returns>Hiçbir şey çalmıyorsa null.</returns>
    public async Task<NowPlaying?> NowPlayingAsync(CancellationToken ct)
    {
        using var doc = await GetAsync("me/player", ct);
        return ParseNowPlaying(doc.RootElement);
    }

    /// <summary>Şarkıyı ya da listeyi/albümü/sanatçıyı açık Spotify cihazında çalar.</summary>
    public async Task PlayAsync(SpotifyItem item, CancellationToken ct)
    {
        object body = item.Kind == SpotifyKind.Track ? new { uris = new[] { item.Uri } } : new { context_uri = item.Uri };
        using var _ = await SendAsync(HttpMethod.Put, "me/player/play", body, ct);
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct) => await SendAsync(HttpMethod.Get, path, null, ct);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var access = await AccessTokenAsync(ct);
        try
        {
            return await SendAsync(http, access, method, Api + path, body, ct);
        }
        catch (SpotifyException) when (_token is null)
        {
            throw;
        }
    }

    private static async Task<JsonDocument> SendAsync(HttpClient http, string access, HttpMethod method, string url, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            else if (method != HttpMethod.Get)
            {
                request.Content = new StringContent("", Encoding.UTF8, "application/json");
            }

            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new SpotifyException(Error(response.StatusCode, text));
            }

            return JsonDocument.Parse(text.Trim().Length == 0 ? "{}" : text);
        }
        catch (HttpRequestException ex)
        {
            throw new SpotifyException($"Spotify'a bağlanılamadı ({ex.Message})");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SpotifyException("Spotify zamanında yanıt vermedi");
        }
        catch (JsonException ex)
        {
            throw new SpotifyException($"Spotify yanıtı okunamadı ({ex.Message})");
        }
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (secrets.Current.Spotify is not { RefreshToken.Length: > 0 } account)
        {
            throw new SpotifyException(NotConnected);
        }

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_token is { } cached && _utcNow() - cached.At < TokenLifetime)
            {
                return cached.Access;
            }

            var (access, refresh) = await TokenAsync(http, new()
            {
                ["client_id"] = account.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = account.RefreshToken,
            }, ct);
            // Spotify yenileme anahtarını değiştirebilir; eskisi bir süre sonra geçersiz olur.
            if (refresh is { Length: > 0 } && refresh != account.RefreshToken)
            {
                secrets.Save(secrets.Current with { Spotify = account with { RefreshToken = refresh } });
            }

            _token = (access, _utcNow());
            return access;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static async Task<(string Access, string? Refresh)> TokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form), ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
            var root = doc.RootElement;
            if (!response.IsSuccessStatusCode || Str(root, "access_token") is not { } access)
            {
                var error = Str(root, "error");
                throw new SpotifyException(error == "invalid_grant"
                    ? "Spotify oturumu geçersiz. Ayarlar → Spotify'dan yeniden bağlan."
                    : error == "invalid_client"
                        ? "Client ID tanınmadı; developer.spotify.com'daki uygulamanın Client ID'sini kontrol et."
                        : $"Spotify giriş hatası ({Str(root, "error_description") ?? error ?? ((int)response.StatusCode).ToString()})");
            }

            return (access, Str(root, "refresh_token"));
        }
        catch (HttpRequestException ex)
        {
            throw new SpotifyException($"Spotify'a bağlanılamadı ({ex.Message})");
        }
        catch (JsonException ex)
        {
            throw new SpotifyException($"Spotify yanıtı okunamadı ({ex.Message})");
        }
    }

    internal static string Error(HttpStatusCode status, string body)
    {
        string? reason = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.TryGetProperty("error", out var e) ? e : default;
            reason = Str(error, "reason");
            message = Str(error, "message");
        }
        catch (JsonException)
        {
        }

        return reason switch
        {
            "NO_ACTIVE_DEVICE" => "Açık bir Spotify cihazı yok. Spotify'ı aç, bir şey çalıp durdur, sonra tekrar dene.",
            "PREMIUM_REQUIRED" => "Spotify'dan çalmak için Premium gerekiyor.",
            _ when status == HttpStatusCode.Unauthorized => "Spotify oturumu geçersiz. Ayarlar → Spotify'dan yeniden bağlan.",
            _ when status == HttpStatusCode.Forbidden =>
                $"Spotify izin vermedi ({message ?? "403"}). Geliştirici panelinde uygulamanın User Management bölümüne kendi e-postanı ekle.",
            _ when status == HttpStatusCode.TooManyRequests => "Spotify çok fazla istek dedi; biraz sonra tekrar dene.",
            _ => $"Spotify hata verdi (HTTP {(int)status}{(message is null ? "" : ": " + message)})",
        };
    }

    internal static IReadOnlyList<SpotifyItem> ParseSearch(JsonElement root)
    {
        var results = new List<SpotifyItem>();
        foreach (var t in Items(root.TryGetProperty("tracks", out var tracks) ? tracks : default))
        {
            if (Str(t, "uri") is { } uri && Str(t, "name") is { } name)
            {
                var album = t.TryGetProperty("album", out var a) ? Str(a, "name") : null;
                results.Add(new SpotifyItem(SpotifyKind.Track, name, Join(Artists(t), album), uri, Url(t)));
            }
        }

        foreach (var p in Items(root.TryGetProperty("playlists", out var playlists) ? playlists : default))
        {
            if (Playlist(p) is { } item)
            {
                results.Add(item);
            }
        }

        foreach (var a in Items(root.TryGetProperty("albums", out var albums) ? albums : default))
        {
            if (Str(a, "uri") is { } uri && Str(a, "name") is { } name)
            {
                var year = Str(a, "release_date") is { Length: >= 4 } date ? date[..4] : null;
                results.Add(new SpotifyItem(SpotifyKind.Album, name, Join(Artists(a), year), uri, Url(a)));
            }
        }

        foreach (var a in Items(root.TryGetProperty("artists", out var artists) ? artists : default))
        {
            if (Str(a, "uri") is { } uri && Str(a, "name") is { } name)
            {
                results.Add(new SpotifyItem(SpotifyKind.Artist, name, "Sanatçı", uri, Url(a)));
            }
        }

        return results;
    }

    internal static NowPlaying? ParseNowPlaying(JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object || Str(item, "name") is not { } name)
        {
            return null;
        }

        var device = root.TryGetProperty("device", out var d) ? Str(d, "name") : null;
        var playing = root.TryGetProperty("is_playing", out var p) && p.ValueKind == JsonValueKind.True;
        var progress = root.TryGetProperty("progress_ms", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : 0;
        var duration = item.TryGetProperty("duration_ms", out var du) && du.ValueKind == JsonValueKind.Number ? du.GetInt32() : 0;
        return new NowPlaying(name, string.Join(", ", Artists(item)), playing, device, progress, duration);
    }

    private static SpotifyItem? Playlist(JsonElement p)
    {
        if (Str(p, "uri") is not { } uri || Str(p, "name") is not { } name)
        {
            return null;
        }

        var owner = p.TryGetProperty("owner", out var o) ? Str(o, "display_name") : null;
        // Spotify 2026'da "tracks" alanını "items" olarak yeniden adlandırdı; ikisi de olabilir.
        string? tracks = null;
        foreach (var field in new[] { "items", "tracks" })
        {
            if (p.TryGetProperty(field, out var t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Number)
            {
                tracks = $"{total.GetInt32()} şarkı";
            }
        }

        return new SpotifyItem(SpotifyKind.Playlist, name, Join(owner is null ? [] : [owner], tracks), uri, Url(p));
    }

    private static IEnumerable<string> Artists(JsonElement e) =>
        e.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => Str(x, "name")).OfType<string>()
            : [];

    private static string Join(IEnumerable<string> people, string? extra)
    {
        var who = string.Join(", ", people);
        return extra is null ? who : who.Length == 0 ? extra : $"{who} · {extra}";
    }

    private static string? Url(JsonElement e) => e.TryGetProperty("external_urls", out var u) ? Str(u, "spotify") : null;

    // Arama sonuçlarında silinmiş listeler null gelebiliyor.
    private static IEnumerable<JsonElement> Items(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("items", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Form(Dictionary<string, string> values) =>
        string.Join('&', values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}
