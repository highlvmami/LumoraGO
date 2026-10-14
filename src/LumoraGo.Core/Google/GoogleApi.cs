using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;

namespace LumoraGo.Core.Google;

/// <param name="Name">Kullanıcıya görünen ad, örn. "Google Takvim".</param>
/// <param name="ApiName">Google Cloud kitaplığındaki adı; kapalıysa kullanıcıya bunu etkinleştirmesi söylenir.</param>
public sealed record GoogleService(string Name, string ApiName)
{
    public static readonly GoogleService Calendar = new("Google Takvim", "Google Calendar API");
    public static readonly GoogleService Tasks = new("Google Görevler", "Google Tasks API");
    public static readonly GoogleService Drive = new("Google Drive", "Google Drive API");
    public static readonly GoogleService YouTube = new("YouTube", "YouTube Data API v3");
    public static readonly GoogleService Gmail = new("Gmail", "Gmail API");
}

/// <summary>
/// "Google ile bağlan" ile alınan izinle Google API'lerine istek atar. Erişim anahtarını bir süre saklar, hataları
/// kullanıcının ne yapması gerektiğini söyleyen <see cref="GoogleException"/>'a çevirir.
/// </summary>
public sealed class GoogleApi(HttpClient http, SecretStore secrets, Func<DateTime>? utcNow = null)
{
    /// <summary>Google anahtarları bir saat geçerli; süresi dolmadan yenilensin.</summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(45);

    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private (string Refresh, string Access, DateTime At)? _token;

    public bool IsConnected => secrets.Current.Google is { RefreshToken.Length: > 0 };

    public async Task<JsonDocument> GetAsync(GoogleService service, string url, CancellationToken ct) =>
        await SendAsync(service, HttpMethod.Get, url, null, ct);

    public async Task<JsonDocument> SendAsync(GoogleService service, HttpMethod method, string url, object? body, CancellationToken ct)
    {
        try
        {
            var access = await AccessTokenAsync(ct);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _token = null;
                }

                throw new GoogleException(Error(service, response.StatusCode, text));
            }

            return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleException($"Google'a bağlanılamadı ({ex.Message})");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleException("Google zamanında yanıt vermedi");
        }
        catch (JsonException ex)
        {
            throw new GoogleException($"{service.Name} yanıtı okunamadı ({ex.Message})");
        }
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (secrets.Current.Google is not { RefreshToken.Length: > 0 } account)
        {
            throw new GoogleException(NotConnected);
        }

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_token is { } cached && cached.Refresh == account.RefreshToken && _utcNow() - cached.At < TokenLifetime)
            {
                return cached.Access;
            }

            var access = await GoogleContacts.AccessTokenAsync(http, account, ct);
            _token = (account.RefreshToken, access, _utcNow());
            return access;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public const string NotConnected = "Google bağlı değil. Ayarlar → Kişiler → \"Google ile bağlan\".";

    internal static string Error(GoogleService service, HttpStatusCode status, string body) =>
        status == HttpStatusCode.Forbidden && body.Contains("SERVICE_DISABLED", StringComparison.Ordinal)
            ? $"Google Cloud projesinde {service.ApiName} açık değil; console.cloud.google.com → üstteki aramaya \"{service.ApiName}\" yaz → Etkinleştir."
            : status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden && body.Contains("SCOPE_INSUFFICIENT", StringComparison.Ordinal)
                ? $"{service.Name} izni yok. Ayarlar → Kişiler'de \"Google ile bağlan\"a yeniden bas ve izin ekranındaki tüm kutucukları işaretle."
                : $"{service.Name} okunamadı (HTTP {(int)status}{(GoogleContacts.GoogleMessage(body) is { } m ? ": " + m : "")}).";
}

internal static class Json
{
    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Items(this JsonElement e, string name = "items") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
}
