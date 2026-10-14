using System.Globalization;
using System.Text.Json;
using LumoraGo.Core.Calendar;
using LumoraGo.Core.Contacts;

namespace LumoraGo.Core.Google;

/// <summary>Google Takvim'in ana takvimi: etkinlikleri okur ve yeni etkinlik ekler.</summary>
public sealed class GoogleCalendar(GoogleApi api, TimeZoneInfo? zone = null) : ICalendarSource
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.events";
    private const string Events = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public bool IsConfigured => api.IsConnected;

    public async Task<IReadOnlyList<CalendarItem>> GetAsync(DateTime fromLocal, DateTime toLocal, Action<string>? errors, CancellationToken ct)
    {
        try
        {
            var url = $"{Events}?singleEvents=true&orderBy=startTime&maxResults=250"
                + "&timeMin=" + Uri.EscapeDataString(Utc(fromLocal)) + "&timeMax=" + Uri.EscapeDataString(Utc(toLocal));
            using var doc = await api.GetAsync(GoogleService.Calendar, url, ct);
            return Parse(doc.RootElement, _zone);
        }
        catch (GoogleException ex)
        {
            errors?.Invoke(ex.Message);
            return [];
        }
    }

    /// <summary>Saat varsa 1 saatlik, yoksa tüm gün etkinlik ekler.</summary>
    /// <exception cref="GoogleException">Eklenemedi.</exception>
    public async Task AddAsync(string title, DateTime when, bool hasTime, CancellationToken ct)
    {
        using var _ = await api.SendAsync(GoogleService.Calendar, HttpMethod.Post, Events, Body(title, when, hasTime, _zone), ct);
    }

    internal static object Body(string title, DateTime when, bool hasTime, TimeZoneInfo zone) => hasTime
        ? new
        {
            summary = title,
            start = new { dateTime = Local(when, zone) },
            end = new { dateTime = Local(when.AddHours(1), zone) },
        }
        : new
        {
            summary = title,
            start = new { date = when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            end = new { date = when.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
        };

    private static string Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Local(DateTime when, TimeZoneInfo zone) =>
        new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Unspecified), zone.GetUtcOffset(when)).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    internal static IReadOnlyList<CalendarItem> Parse(JsonElement root, TimeZoneInfo zone)
    {
        var items = new List<CalendarItem>();
        foreach (var e in root.Items())
        {
            if (e.Str("status") == "cancelled")
            {
                continue;
            }

            var start = e.Obj("start");
            var end = e.Obj("end");
            var allDay = start.Str("date") is not null;
            if (When(start, zone) is not { } from)
            {
                continue;
            }

            var to = When(end, zone) ?? (allDay ? from.AddDays(1) : from.AddHours(1));
            var location = e.Str("location");
            var meeting = e.Str("hangoutLink")
                ?? e.Obj("conferenceData").Items("entryPoints").Where(p => p.Str("entryPointType") == "video").Select(p => p.Str("uri")).FirstOrDefault()
                ?? CalendarSource.FindMeetingUrl(location, e.Str("description"));
            items.Add(new CalendarItem(e.Str("summary") is { Length: > 0 } title ? title : "(adsız)", from, to, allDay, location, meeting));
        }

        return items;
    }

    private static DateTime? When(JsonElement e, TimeZoneInfo zone)
    {
        if (e.Str("dateTime") is { } dateTime && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return TimeZoneInfo.ConvertTime(at, zone).DateTime;
        }

        return e.Str("date") is { } date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
    }
}

/// <param name="Due">Google Görevler yalnızca günü saklar.</param>
public sealed record TaskItem(string ListId, string ListTitle, string Id, string Title, string? Notes, DateTime? Due);

/// <summary>Google Görevler: tüm listelerdeki yapılacaklar, ekleme ve tamamlama.</summary>
public sealed class GoogleTasks(GoogleApi api)
{
    public const string Scope = "https://www.googleapis.com/auth/tasks";
    private const string Base = "https://tasks.googleapis.com/tasks/v1/";

    public bool IsConnected => api.IsConnected;

    public async Task<IReadOnlyList<TaskItem>> GetAsync(CancellationToken ct)
    {
        using var lists = await api.GetAsync(GoogleService.Tasks, Base + "users/@me/lists?maxResults=100", ct);
        var result = new List<TaskItem>();
        foreach (var list in lists.RootElement.Items().ToList())
        {
            var id = list.Str("id") ?? "";
            var title = list.Str("title") ?? "";
            using var tasks = await api.GetAsync(GoogleService.Tasks, $"{Base}lists/{Uri.EscapeDataString(id)}/tasks?showCompleted=false&showHidden=false&maxResults=100", ct);
            result.AddRange(ParseTasks(tasks.RootElement, id, title));
        }

        return result;
    }

    internal static IEnumerable<TaskItem> ParseTasks(JsonElement root, string listId, string listTitle) =>
        root.Items()
            .Where(t => t.Str("status") != "completed" && t.Str("title") is { Length: > 0 })
            .Select(t => new TaskItem(
                listId, listTitle, t.Str("id") ?? "", t.Str("title")!.Trim(), t.Str("notes"),
                t.Str("due") is { } due && DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime.Date : null));

    /// <summary>Varsayılan listeye ekler.</summary>
    public async Task AddAsync(string title, DateTime? due, CancellationToken ct)
    {
        object body = due is { } day
            ? new { title, due = day.ToString("yyyy-MM-dd'T'00:00:00.000'Z'", CultureInfo.InvariantCulture) }
            : new { title };
        using var _ = await api.SendAsync(GoogleService.Tasks, HttpMethod.Post, Base + "lists/@default/tasks", body, ct);
    }

    public async Task CompleteAsync(TaskItem task, CancellationToken ct)
    {
        var url = $"{Base}lists/{Uri.EscapeDataString(task.ListId)}/tasks/{Uri.EscapeDataString(task.Id)}";
        using var _ = await api.SendAsync(GoogleService.Tasks, HttpMethod.Patch, url, new { status = "completed" }, ct);
    }
}

public sealed record DriveFile(string Name, string Url, string Kind, DateTime? Modified);

/// <summary>Google Drive'da dosya adına göre arama (dosyaların içeriği okunmaz).</summary>
public sealed class GoogleDrive(GoogleApi api)
{
    public const string Scope = "https://www.googleapis.com/auth/drive.metadata.readonly";
    private const string Files = "https://www.googleapis.com/drive/v3/files";

    /// <param name="text">Boşsa son açılanlar.</param>
    public async Task<IReadOnlyList<DriveFile>> SearchAsync(string text, CancellationToken ct)
    {
        var q = "trashed = false" + (text.Length > 0 ? $" and name contains '{text.Replace("\\", "\\\\").Replace("'", "\\'")}'" : "");
        var url = $"{Files}?pageSize=30&spaces=drive&fields=files(name,mimeType,webViewLink,modifiedTime)"
            + "&orderBy=" + Uri.EscapeDataString(text.Length > 0 ? "modifiedTime desc" : "viewedByMeTime desc")
            + "&q=" + Uri.EscapeDataString(q);
        using var doc = await api.GetAsync(GoogleService.Drive, url, ct);
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<DriveFile> Parse(JsonElement root) =>
        root.Items("files")
            .Where(f => f.Str("webViewLink") is not null)
            .Select(f => new DriveFile(
                f.Str("name") ?? "",
                f.Str("webViewLink")!,
                Kind(f.Str("mimeType")),
                f.Str("modifiedTime") is { } m && DateTimeOffset.TryParse(m, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.LocalDateTime : null))
            .ToList();

    internal static string Kind(string? mime) => mime switch
    {
        "application/vnd.google-apps.document" => "Doküman",
        "application/vnd.google-apps.spreadsheet" => "Tablo",
        "application/vnd.google-apps.presentation" => "Sunu",
        "application/vnd.google-apps.form" => "Form",
        "application/vnd.google-apps.folder" => "Klasör",
        "application/pdf" => "PDF",
        { } m when m.StartsWith("image/", StringComparison.Ordinal) => "Resim",
        { } m when m.StartsWith("video/", StringComparison.Ordinal) => "Video",
        { } m when m.StartsWith("audio/", StringComparison.Ordinal) => "Ses",
        _ => "Dosya",
    };
}

public enum YouTubeKind
{
    Channel,
    Playlist,
    Video,
}

/// <param name="Detail">Kanal açıklaması, liste video sayısı ya da videonun kanalı.</param>
public sealed record YouTubeItem(YouTubeKind Kind, string Title, string Url, string Detail);

/// <summary>
/// Kendi YouTube kütüphanen: abonelikler, oynatma listeleri ve beğenilen videolar. Diskte saklanır, birkaç saatte bir yenilenir.
/// "Sonra izle" listesini Google API'ye kapattığı için gösterilemez.
/// </summary>
public sealed class YouTubeLibrary
{
    public const string Scope = "https://www.googleapis.com/auth/youtube.readonly";
    private const string Base = "https://www.googleapis.com/youtube/v3/";
    private const int MaxLiked = 200;

    private readonly GoogleApi _api;
    private readonly string _cachePath;
    private readonly ILog _log;
    private readonly object _gate = new();
    private IReadOnlyList<YouTubeItem> _items;
    private int _refreshing;

    public YouTubeLibrary(GoogleApi api, string cachePath, ILog? log = null)
    {
        _api = api;
        _cachePath = cachePath;
        _log = log ?? NullLog.Instance;
        _items = Load();
    }

    public bool IsConnected => _api.IsConnected;

    public IReadOnlyList<YouTubeItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items;
            }
        }
    }

    public DateTime? RefreshedAt => File.Exists(_cachePath) ? File.GetLastWriteTimeUtc(_cachePath) : null;

    public bool IsRefreshing => Volatile.Read(ref _refreshing) == 1;

    /// <returns>Alınan öğe sayısı; zaten yenileniyorsa null.</returns>
    /// <exception cref="GoogleException">YouTube okunamadı.</exception>
    public async Task<int?> RefreshAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return null;
        }

        try
        {
            var items = new List<YouTubeItem>();
            await PagesAsync(Base + "subscriptions?mine=true&part=snippet&maxResults=50&order=alphabetical", 1000, root =>
                items.AddRange(root.Items().Select(s => s.Obj("snippet")).Select(s => new YouTubeItem(
                    YouTubeKind.Channel, s.Str("title") ?? "", "https://www.youtube.com/channel/" + s.Obj("resourceId").Str("channelId"), "Abone olduğun kanal"))), ct);
            await PagesAsync(Base + "playlists?mine=true&part=snippet,contentDetails&maxResults=50", 500, root =>
                items.AddRange(root.Items().Select(p => new YouTubeItem(
                    YouTubeKind.Playlist, p.Obj("snippet").Str("title") ?? "", "https://www.youtube.com/playlist?list=" + p.Str("id"),
                    $"Oynatma listen · {Count(p.Obj("contentDetails"))} video"))), ct);

            using (var channel = await _api.GetAsync(GoogleService.YouTube, Base + "channels?mine=true&part=contentDetails", ct))
            {
                if (channel.RootElement.Items().FirstOrDefault().Obj("contentDetails").Obj("relatedPlaylists").Str("likes") is { Length: > 0 } likes)
                {
                    await PagesAsync(Base + "playlistItems?part=snippet&maxResults=50&playlistId=" + Uri.EscapeDataString(likes), MaxLiked, root =>
                        items.AddRange(ParseVideos(root)), ct);
                }
            }

            lock (_gate)
            {
                _items = items;
            }

            Save(items);
            _log.Info($"YouTube: {items.Count} öğe alındı.");
            return items.Count;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    internal static IEnumerable<YouTubeItem> ParseVideos(JsonElement root) =>
        root.Items().Select(v => v.Obj("snippet"))
            .Where(s => s.Obj("resourceId").Str("videoId") is not null && s.Str("title") is { } t && t != "Deleted video" && t != "Private video")
            .Select(s => new YouTubeItem(
                YouTubeKind.Video, s.Str("title")!, "https://www.youtube.com/watch?v=" + s.Obj("resourceId").Str("videoId"),
                $"Beğendiğin video · {s.Str("videoOwnerChannelTitle") ?? ""}".TrimEnd(' ', '·')));

    private static int Count(JsonElement details) =>
        details.ValueKind == JsonValueKind.Object && details.TryGetProperty("itemCount", out var c) && c.TryGetInt32(out var n) ? n : 0;

    private async Task PagesAsync(string url, int max, Action<JsonElement> add, CancellationToken ct)
    {
        string? page = null;
        var seen = 0;
        do
        {
            using var doc = await _api.GetAsync(GoogleService.YouTube, url + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), ct);
            add(doc.RootElement);
            seen += doc.RootElement.Items().Count();
            page = doc.RootElement.Str("nextPageToken");
        }
        while (page is { Length: > 0 } && seen < max);
    }

    private IReadOnlyList<YouTubeItem> Load()
    {
        try
        {
            return File.Exists(_cachePath) ? JsonSerializer.Deserialize<List<YouTubeItem>>(File.ReadAllText(_cachePath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Warn($"YouTube önbelleği okunamadı: {ex.Message}");
            return [];
        }
    }

    private void Save(IReadOnlyList<YouTubeItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath + ".tmp", JsonSerializer.Serialize(items));
            File.Move(_cachePath + ".tmp", _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"YouTube önbelleği kaydedilemedi: {ex.Message}");
        }
    }
}
