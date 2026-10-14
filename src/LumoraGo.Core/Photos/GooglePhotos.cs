using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;

namespace LumoraGo.Core.Photos;

/// <summary>
/// Google Fotoğraflar seçicisi (Photos Picker API). Google, Mart 2025'ten beri uygulamaların kütüphanenin tamamını
/// okumasına izin vermiyor; kullanıcı Google'ın kendi sayfasında fotoğraf seçer, uygulama yalnızca seçilenleri alır.
/// </summary>
public static class GooglePhotosPicker
{
    public const string Scope = "https://www.googleapis.com/auth/photospicker.mediaitems.readonly";
    private const string Endpoint = "https://photospicker.googleapis.com/v1/";

    /// <summary>Önizleme için yeterli; tam boy indirmek gereksiz yer kaplar.</summary>
    private const string SizeSuffix = "=w2048-h2048";

    public sealed record Session(string Id, string PickerUri, bool MediaItemsSet, TimeSpan PollInterval, TimeSpan TimeoutIn);

    public sealed record PickedItem(string Id, DateTimeOffset? Created, bool IsVideo, string BaseUrl, string FileName, int Width, int Height);

    public static async Task<Session> CreateSessionAsync(HttpClient http, string accessToken, CancellationToken ct) =>
        ParseSession(await SendAsync(http, HttpMethod.Post, Endpoint + "sessions", accessToken, ct));

    public static async Task<Session> GetSessionAsync(HttpClient http, string accessToken, string id, CancellationToken ct) =>
        ParseSession(await SendAsync(http, HttpMethod.Get, Endpoint + "sessions/" + Uri.EscapeDataString(id), accessToken, ct));

    public static async Task DeleteSessionAsync(HttpClient http, string accessToken, string id)
    {
        try
        {
            await SendAsync(http, HttpMethod.Delete, Endpoint + "sessions/" + Uri.EscapeDataString(id), accessToken, CancellationToken.None);
        }
        catch (Exception ex) when (ex is GoogleException or HttpRequestException or TaskCanceledException)
        {
            // Silinemeyen oturum Google'da kendiliğinden sona erer.
        }
    }

    public static async Task<IReadOnlyList<PickedItem>> ListItemsAsync(HttpClient http, string accessToken, string sessionId, CancellationToken ct)
    {
        var items = new List<PickedItem>();
        string? page = null;
        do
        {
            var url = Endpoint + "mediaItems?pageSize=100&sessionId=" + Uri.EscapeDataString(sessionId)
                + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            var (batch, next) = ParseItems(await SendAsync(http, HttpMethod.Get, url, accessToken, ct));
            items.AddRange(batch);
            page = next;
        }
        while (page is { Length: > 0 });

        return items;
    }

    /// <summary>Fotoğrafı (videoysa kapak karesini) indirir.</summary>
    /// <returns>Google'ın gönderdiği biçime uyan dosya uzantısı (".jpg" gibi) ve baytlar.</returns>
    public static async Task<(string Extension, byte[] Bytes)> DownloadAsync(HttpClient http, string accessToken, PickedItem item, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, item.BaseUrl + SizeSuffix);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleException(ApiError(response.StatusCode, await response.Content.ReadAsStringAsync(ct)));
        }

        return (Extension(response.Content.Headers.ContentType?.MediaType, item.FileName), await response.Content.ReadAsByteArrayAsync(ct));
    }

    internal static string Extension(string? mediaType, string fileName) => mediaType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/heic" or "image/heif" => ".heic",
        _ => Path.GetExtension(fileName) is { Length: > 1 } ext && !ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                ? ext.ToLowerInvariant()
                : ".jpg",
    };

    internal static Session ParseSession(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var polling = root.TryGetProperty("pollingConfig", out var p) ? p : default;
        return new Session(
            root.GetProperty("id").GetString() ?? "",
            root.TryGetProperty("pickerUri", out var uri) ? uri.GetString() ?? "" : "",
            root.TryGetProperty("mediaItemsSet", out var set) && set.ValueKind == JsonValueKind.True,
            Duration(polling, "pollInterval") ?? TimeSpan.FromSeconds(3),
            Duration(polling, "timeoutIn") ?? TimeSpan.FromMinutes(10));
    }

    /// <summary>Google süreleri "3.5s" biçiminde yazar.</summary>
    private static TimeSpan? Duration(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.GetString() is { } text
        && text.EndsWith('s') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    internal static (IReadOnlyList<PickedItem> Items, string? NextPage) ParseItems(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = new List<PickedItem>();
        if (root.TryGetProperty("mediaItems", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("mediaFile", out var file) || !file.TryGetProperty("baseUrl", out var baseUrl))
                {
                    continue;
                }

                var meta = file.TryGetProperty("mediaFileMetadata", out var m) ? m : default;
                items.Add(new PickedItem(
                    item.GetProperty("id").GetString() ?? "",
                    item.TryGetProperty("createTime", out var c) && DateTimeOffset.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)
                        ? created
                        : null,
                    item.TryGetProperty("type", out var t) && t.GetString() == "VIDEO",
                    baseUrl.GetString() ?? "",
                    file.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "",
                    Int(meta, "width"),
                    Int(meta, "height")));
            }
        }

        return (items, root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null);
    }

    private static int Int(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.TryParse(v.GetString(), out var n) ? n : 0
            : 0;

    private static async Task<string> SendAsync(HttpClient http, HttpMethod method, string url, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode ? body : throw new GoogleException(ApiError(response.StatusCode, body));
    }

    public const string NoPhotosScope =
        "Google Fotoğraflar izni yok. Ayarlar → Kişiler'de \"Google ile bağlan\"a yeniden bas ve izin ekranında fotoğraflar kutucuğunu da işaretle.";

    internal static string ApiError(HttpStatusCode status, string body) =>
        status == HttpStatusCode.Forbidden && body.Contains("SERVICE_DISABLED", StringComparison.Ordinal)
            ? "Google Cloud projesinde Photos Picker API açık değil; console.cloud.google.com → API'ler → \"Google Photos Picker API\" → Etkinleştir."
            : status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden && body.Contains("SCOPE_INSUFFICIENT", StringComparison.Ordinal)
                ? NoPhotosScope
                : $"Google Fotoğraflar okunamadı (HTTP {(int)status}{(GoogleContacts.GoogleMessage(body) is { } m ? ": " + m : "")}).";
}

/// <summary>Launcher'da gösterilen bir fotoğraf; Google'dan seçilip indirilmiş ya da bilgisayardaki klasörden.</summary>
public sealed record Photo(string Path, string Name, DateTime Date, bool FromGoogle, string? GoogleId = null);

/// <summary>
/// Google'dan seçilen fotoğrafları kendi klasöründe saklar ve bilgisayardaki fotoğraf klasörünü tarar.
/// Önizleme ve küçük resimler yerel dosyadan çizilir, böylece Google bağlantısı yokken de çalışır.
/// </summary>
public sealed class PhotoLibrary
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp", ".tif", ".tiff", ".jfif"];

    /// <summary>Çok büyük arşivlerde tarama uzamasın; en yeniler yeter.</summary>
    public const int MaxLocal = 5000;

    private static readonly TimeSpan LocalRefresh = TimeSpan.FromMinutes(5);

    private readonly string _googleDir;
    private readonly string _indexPath;
    private readonly Func<string?> _localFolder;
    private readonly ILog _log;
    private readonly object _gate = new();
    private List<Photo> _google;
    private List<Photo> _local = [];
    private DateTime _localScanned = DateTime.MinValue;
    private string? _scannedFolder;
    private int _scanning;

    /// <param name="googleDir">Google'dan indirilen fotoğrafların klasörü.</param>
    /// <param name="localFolder">Taranacak bilgisayar klasörü; null ise yalnızca Google fotoğrafları.</param>
    public PhotoLibrary(string googleDir, Func<string?> localFolder, ILog? log = null)
    {
        _googleDir = googleDir;
        _indexPath = System.IO.Path.Combine(googleDir, "index.json");
        _localFolder = localFolder;
        _log = log ?? NullLog.Instance;
        _google = LoadIndex();
    }

    public string GoogleDirectory => _googleDir;

    public IReadOnlyList<Photo> Google
    {
        get
        {
            lock (_gate)
            {
                return _google.ToList();
            }
        }
    }

    /// <summary>Son tarama sonucu; eskiyse arka planda yeniden tarar ve bitince <paramref name="scanned"/> çağrılır.</summary>
    public IReadOnlyList<Photo> Local(Action? scanned = null)
    {
        var folder = _localFolder();
        lock (_gate)
        {
            if (folder != _scannedFolder || DateTime.UtcNow - _localScanned > LocalRefresh)
            {
                StartScan(folder, scanned);
            }

            return _local;
        }
    }

    /// <summary>Test ve ilk açılış için: taramayı bekler.</summary>
    public void ScanNow()
    {
        var folder = _localFolder();
        var found = Scan(folder);
        lock (_gate)
        {
            _local = found;
            _scannedFolder = folder;
            _localScanned = DateTime.UtcNow;
        }
    }

    private void StartScan(string? folder, Action? scanned)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var found = Scan(folder);
                lock (_gate)
                {
                    _local = found;
                    _scannedFolder = folder;
                    _localScanned = DateTime.UtcNow;
                }

                scanned?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        });
    }

    private List<Photo> Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | (FileAttributes)0x00400000 };
            return new DirectoryInfo(folder).EnumerateFiles("*", options)
                .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                // Google'dan indirilenler zaten ayrıca listelenir.
                .Where(f => !f.FullName.StartsWith(_googleDir, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(MaxLocal)
                .Select(f => new Photo(f.FullName, f.Name, f.LastWriteTime, false))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Warn($"Fotoğraf klasörü taranamadı: {folder}: {ex.Message}");
            return [];
        }
    }

    public bool HasGoogle(string id)
    {
        lock (_gate)
        {
            return _google.Any(p => p.GoogleId == id);
        }
    }

    /// <summary>İndirilen fotoğrafı kaydeder. Aynı Google fotoğrafı yeniden seçilirse eskisinin yerine geçer.</summary>
    public Photo AddGoogle(string id, string name, DateTime date, string extension, byte[] bytes)
    {
        Directory.CreateDirectory(_googleDir);
        var safeId = string.Concat(id.Where(char.IsAsciiLetterOrDigit));
        if (safeId.Length > 40)
        {
            safeId = safeId[..40];
        }

        var path = System.IO.Path.Combine(_googleDir, $"{date:yyyyMMdd-HHmmss}-{safeId}{extension}");
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTime(path, date);
        var photo = new Photo(path, string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(path) : name, date, true, id);
        lock (_gate)
        {
            _google = [photo, .. _google.Where(p => p.GoogleId != id)];
            SaveIndex();
        }

        return photo;
    }

    public void RemoveGoogle(Photo photo)
    {
        lock (_gate)
        {
            _google = _google.Where(p => p.Path != photo.Path).ToList();
            SaveIndex();
        }

        TryDelete(photo.Path);
    }

    public void ClearGoogle()
    {
        List<Photo> removed;
        lock (_gate)
        {
            removed = _google;
            _google = [];
            SaveIndex();
        }

        foreach (var photo in removed)
        {
            TryDelete(photo.Path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Fotoğraf silinemedi: {path}: {ex.Message}");
        }
    }

    private sealed record IndexEntry(string Path, string Name, DateTime Date, string Id);

    private List<Photo> LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<IndexEntry>>(File.ReadAllText(_indexPath)) ?? [];
            return entries.Where(e => File.Exists(e.Path)).Select(e => new Photo(e.Path, e.Name, e.Date, true, e.Id)).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Warn($"Google fotoğraf listesi okunamadı: {ex.Message}");
            return [];
        }
    }

    private void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(_googleDir);
            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_google.Select(p => new IndexEntry(p.Path, p.Name, p.Date, p.GoogleId ?? ""))));
            File.Move(temp, _indexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Google fotoğraf listesi kaydedilemedi: {ex.Message}");
        }
    }
}

/// <summary>Google Fotoğraflar seçicisini açar, seçilenleri indirir. Aynı anda tek seçim çalışır.</summary>
public sealed class GooglePhotoImport(HttpClient http, SecretStore secrets, PhotoLibrary library, Action<string> openBrowser, ILog? log = null)
{
    private readonly ILog _log = log ?? NullLog.Instance;
    private int _running;

    /// <summary>Test için bekleme; gerçek kullanımda Google'ın önerdiği aralık.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <returns>Eklenen fotoğraf sayısı; seçim zaten sürüyorsa null.</returns>
    /// <exception cref="GoogleException">Bağlı değil, izin yok ya da Google'a ulaşılamadı.</exception>
    public async Task<int?> PickAsync(CancellationToken ct)
    {
        if (secrets.Current.Google is not { RefreshToken.Length: > 0 } account)
        {
            throw new GoogleException("Önce Ayarlar → Kişiler'den \"Google ile bağlan\".");
        }

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return null;
        }

        string? access = null;
        GooglePhotosPicker.Session? session = null;
        try
        {
            access = await GoogleContacts.AccessTokenAsync(http, account, ct);
            session = await GooglePhotosPicker.CreateSessionAsync(http, access, ct);
            openBrowser(session.PickerUri.TrimEnd('/') + "/autoclose");

            var deadline = DateTime.UtcNow + session.TimeoutIn;
            while (!session.MediaItemsSet)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new GoogleException("Seçim süresi doldu; yeniden dene.");
                }

                await Delay(session.PollInterval, ct);
                session = await GooglePhotosPicker.GetSessionAsync(http, access, session.Id, ct);
            }

            var items = await GooglePhotosPicker.ListItemsAsync(http, access, session.Id, ct);
            var added = 0;
            foreach (var item in items)
            {
                var (extension, bytes) = await GooglePhotosPicker.DownloadAsync(http, access, item, ct);
                var date = (item.Created ?? DateTimeOffset.Now).LocalDateTime;
                library.AddGoogle(item.Id, item.FileName, date, extension, bytes);
                added++;
            }

            _log.Info($"Google Fotoğraflar: {added} fotoğraf eklendi.");
            return added;
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleException($"Google'a bağlanılamadı ({ex.Message})");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleException("Google zamanında yanıt vermedi");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            _log.Error($"Google Fotoğraflar işlenemedi: {ex}");
            throw new GoogleException($"Google Fotoğraflar işlenemedi ({ex.GetType().Name}: {ex.Message})");
        }
        finally
        {
            if (access is not null && session is not null)
            {
                await GooglePhotosPicker.DeleteSessionAsync(http, access, session.Id);
            }

            Interlocked.Exchange(ref _running, 0);
        }
    }
}
