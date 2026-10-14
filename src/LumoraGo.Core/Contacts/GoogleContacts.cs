using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumoraGo.Core.Accounts;

namespace LumoraGo.Core.Contacts;

public sealed class GoogleException(string message) : Exception(message);

/// <summary>
/// Google ile masaüstü girişi (OAuth, PKCE ve 127.0.0.1 dönüşü) ve Google Kişiler'i (People API) okuma.
/// Yalnızca okuma izni istenir; rehberde hiçbir şey değiştirilmez.
/// </summary>
public static class GoogleContacts
{
    public const string Scope = "https://www.googleapis.com/auth/contacts.readonly https://www.googleapis.com/auth/userinfo.email "
        + Photos.GooglePhotosPicker.Scope;
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ConnectionsEndpoint = "https://people.googleapis.com/v1/people/me/connections";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    public static string AuthUrl(string clientId, string redirectUri, string codeChallenge, string state) =>
        AuthEndpoint
        + "?client_id=" + Uri.EscapeDataString(clientId)
        + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
        + "&response_type=code"
        + "&scope=" + Uri.EscapeDataString(Scope)
        + "&code_challenge=" + codeChallenge
        + "&code_challenge_method=S256"
        + "&state=" + state
        // Yenileme anahtarı yalnızca offline + consent ile her seferinde gelir.
        + "&access_type=offline&prompt=consent";

    /// <summary>PKCE için rastgele doğrulayıcı ve onun SHA-256 özeti.</summary>
    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    /// <summary>Tarayıcıda Google girişini açar, 127.0.0.1'e dönen kodu alır ve yenileme anahtarıyla hesabı döndürür.</summary>
    /// <param name="openBrowser">Adresi varsayılan tarayıcıda açar.</param>
    public static async Task<GoogleAccount> SignInAsync(HttpClient http, string clientId, string clientSecret, Action<string> openBrowser, CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var (verifier, challenge) = NewPkce();
        var state = NewState();
        openBrowser(AuthUrl(clientId, redirect, challenge, state));

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(5));
        string code;
        try
        {
            code = await ReceiveCodeAsync(listener, state, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleException("Google girişi 5 dakika içinde tamamlanmadı.");
        }

        var token = await TokenAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirect,
        }, ct);
        // Google izin ekranında kişiler kutucuğu işaretsiz bırakılabiliyor; o zaman giriş başarılı görünür ama rehber okunamaz.
        if (token.Scope is { } granted && !granted.Split(' ').Contains("https://www.googleapis.com/auth/contacts.readonly"))
        {
            throw new GoogleException(NoContactsScope);
        }

        if (token.Refresh is not { Length: > 0 } refresh)
        {
            throw new GoogleException("Google yenileme anahtarı vermedi; myaccount.google.com/permissions'tan LumoraGo erişimini kaldırıp yeniden bağlan.");
        }

        var email = await EmailAsync(http, token.Access, ct);
        return new GoogleAccount(clientId, clientSecret, refresh, email);
    }

    /// <summary>Tarayıcının 127.0.0.1'e yaptığı ilk "GET /?code=...&state=..." isteğini okuyup kısa bir sayfa döndürür.</summary>
    internal static async Task<string> ReceiveCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            var requestLine = await ReadRequestLineAsync(stream, ct);
            var target = requestLine.Split(' ') is [_, var t, ..] ? t : "/";
            var query = ParseQuery(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");

            string? error = null;
            string? code = null;
            if (query.GetValueOrDefault("state") != state)
            {
                // Tarayıcının favicon gibi başka istekleri; yanıtlayıp beklemeye devam.
                if (!query.ContainsKey("code") && !query.ContainsKey("error"))
                {
                    await RespondAsync(stream, 404, "", ct);
                    Finish(client);
                    continue;
                }

                error = "Güvenlik denetimi tutmadı (state). Ayarlardan yeniden dene.";
            }
            else if (query.GetValueOrDefault("error") is { } e)
            {
                error = e == "access_denied" ? "Google erişimine izin verilmedi." : $"Google hata verdi: {e}";
            }
            else
            {
                code = query.GetValueOrDefault("code");
                error = code is { Length: > 0 } ? null : "Google kod göndermedi.";
            }

            await RespondAsync(stream, 200, Page(error ?? "LumoraGo Google Rehber'e bağlandı. Bu sekmeyi kapatabilirsin."), ct);
            Finish(client);
            return error is null ? code! : throw new GoogleException(error);
        }
    }

    /// <summary>Kayıtlı yenileme anahtarıyla kısa ömürlü erişim anahtarı alır.</summary>
    public static async Task<string> AccessTokenAsync(HttpClient http, GoogleAccount account, CancellationToken ct) =>
        (await TokenAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = account.ClientId,
            ["client_secret"] = account.ClientSecret,
            ["refresh_token"] = account.RefreshToken,
            ["grant_type"] = "refresh_token",
        }, ct)).Access;

    /// <summary>Rehberdeki herkes, sayfa sayfa (Google sayfa başına en fazla 1000 kişi verir).</summary>
    public static async Task<IReadOnlyList<Contact>> FetchAsync(HttpClient http, string accessToken, CancellationToken ct)
    {
        var contacts = new List<Contact>();
        string? page = null;
        do
        {
            var url = ConnectionsEndpoint + "?personFields=names,emailAddresses,phoneNumbers,organizations&pageSize=1000&sortOrder=FIRST_NAME_ASCENDING"
                + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new GoogleException(ApiError(response.StatusCode, body));
            }

            var (batch, next) = ParseConnections(body);
            contacts.AddRange(batch);
            page = next;
        }
        while (page is { Length: > 0 });

        return contacts;
    }

    internal static (IReadOnlyList<Contact> Contacts, string? NextPage) ParseConnections(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var contacts = new List<Contact>();
        if (root.TryGetProperty("connections", out var people) && people.ValueKind == JsonValueKind.Array)
        {
            foreach (var person in people.EnumerateArray())
            {
                var phones = Values(person, "phoneNumbers", "value");
                var emails = Values(person, "emailAddresses", "value");
                var org = Values(person, "organizations", "name").FirstOrDefault();
                var name = Values(person, "names", "displayName").FirstOrDefault()
                    ?? org ?? emails.FirstOrDefault() ?? phones.FirstOrDefault();
                if (name is null)
                {
                    continue;
                }

                contacts.Add(new Contact(name, phones, emails, org, FromGoogle: true));
            }
        }

        var next = root.TryGetProperty("nextPageToken", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return (contacts, next);
    }

    private static List<string> Values(JsonElement person, string list, string field)
    {
        var result = new List<string>();
        if (person.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
                    && !result.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(s.Trim());
                }
            }
        }

        return result;
    }

    private const string NoContactsScope =
        "Google'da kişileri görme izni verilmedi. \"Google ile bağlan\"a yeniden bas ve izin ekranındaki tüm kutucukları işaretle.";

    private sealed record Token(string Access, string? Refresh, string? Scope);

    private static async Task<Token> TokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleException(TokenError(body));
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new Token(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("scope", out var sc) ? sc.GetString() : null);
    }

    private static async Task<string> EmailAsync(HttpClient http, string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return "";
        }
    }

    internal static string TokenError(string body)
    {
        var error = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
        }

        return error switch
        {
            "invalid_grant" => "Google bağlantısının süresi dolmuş ya da iptal edilmiş; Ayarlar → Kişiler'den yeniden \"Google ile bağlan\".",
            "invalid_client" or "unauthorized_client" => "İstemci kimliği ya da gizli anahtar yanlış; Google Cloud'daki masaüstü istemcisinden yeniden kopyala.",
            _ => $"Google girişi başarısız ({(error.Length > 0 ? error : "bilinmeyen hata")}).",
        };
    }

    internal static string ApiError(HttpStatusCode status, string body) =>
        status == HttpStatusCode.Forbidden && body.Contains("SERVICE_DISABLED", StringComparison.Ordinal)
            ? "Google Cloud projesinde People API açık değil; console.cloud.google.com → API'ler → People API → Etkinleştir."
            : status == HttpStatusCode.Forbidden && body.Contains("SCOPE_INSUFFICIENT", StringComparison.Ordinal)
                ? NoContactsScope
            : status == HttpStatusCode.Unauthorized
                ? "Google erişimi reddetti; Ayarlar → Kişiler'den yeniden bağlan."
                : $"Google Kişiler okunamadı (HTTP {(int)status}{(GoogleMessage(body) is { } m ? ": " + m : "")}).";

    /// <summary>Google'ın hata gövdesindeki açıklama; kayıtta ve ayarlarda neyin ters gittiği görünsün.</summary>
    internal static string? GoogleMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text
                ? text
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            result[key] = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }

        return result;
    }

    /// <summary>
    /// İsteğin ilk satırını döndürür ama başlıkların hepsini (boş satıra kadar) okur: okunmamış veri varken
    /// soket kapatılırsa Windows bağlantıyı sıfırlar ve tarayıcı sayfa yerine hata gösterir.
    /// </summary>
    private static async Task<string> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 65536 && await stream.ReadAsync(one, ct) == 1)
        {
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^1] == '\n' && bytes[^2] == '\r' && bytes[^3] == '\n' && bytes[^4] == '\r')
            {
                break;
            }
        }

        var text = Encoding.ASCII.GetString([.. bytes]);
        var end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }

    private static async Task RespondAsync(Stream stream, int status, string html, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Yanıt gittikten sonra yazma yönünü kapatır, böylece tarayıcı sayfanın tamamını alır.</summary>
    private static void Finish(TcpClient client)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
        }
    }

    private static string Page(string message) =>
        "<!doctype html><meta charset=\"utf-8\"><title>LumoraGo</title>"
        + "<body style=\"font-family:Segoe UI,sans-serif;background:#1e1b2e;color:#eee;display:grid;place-items:center;height:100vh;margin:0\">"
        + $"<p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p></body>";

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Google Rehber'i düzenli olarak rehbere aktarır. Aynı anda tek eşitleme çalışır.</summary>
public sealed class GoogleContactSync(HttpClient http, SecretStore secrets, ContactBook book, ILog? log = null)
{
    private readonly ILog _log = log ?? NullLog.Instance;
    private int _running;

    /// <returns>Google'dan gelen kişi sayısı; bağlı değilse ya da eşitleme sürüyorsa null.</returns>
    /// <exception cref="GoogleException">Google erişimi ya da okuma başarısız.</exception>
    public async Task<int?> SyncAsync(CancellationToken ct)
    {
        if (secrets.Current.Google is not { RefreshToken.Length: > 0 } account || Interlocked.Exchange(ref _running, 1) == 1)
        {
            return null;
        }

        try
        {
            var access = await GoogleContacts.AccessTokenAsync(http, account, ct);
            var contacts = await GoogleContacts.FetchAsync(http, access, ct);
            book.ReplaceGoogle(contacts);
            _log.Info($"Google Rehber: {contacts.Count} kişi alındı.");
            return contacts.Count;
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleException($"Google'a bağlanılamadı ({ex.Message})");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleException("Google zamanında yanıt vermedi");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _log.Error($"Google Rehber yanıtı işlenemedi: {ex}");
            throw new GoogleException($"Google'ın yanıtı okunamadı ({ex.GetType().Name}: {ex.Message})");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
