using System.Text.RegularExpressions;
using Ical.Net;
using IcalCalendar = Ical.Net.Calendar;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace LumoraGo.Core.Calendar;

/// <param name="Start">Yerel saatle başlangıç (tüm gün etkinliğinde günün başı).</param>
/// <param name="MeetingUrl">Konum ya da açıklamada bulunan Meet/Teams/Zoom bağlantısı.</param>
public sealed record CalendarItem(string Title, DateTime Start, DateTime End, bool AllDay, string? Location, string? MeetingUrl);

/// <summary>
/// ICS adreslerinden (Google Takvim "gizli adres", Outlook "takvim yayınla") etkinlikleri okur. Tekrarlayan etkinlikler açılır,
/// iptal edilenler atlanır. İndirilen dosya kısa süre önbellekte tutulur.
/// </summary>
public sealed partial class CalendarSource
{
    private readonly HttpClient _http;
    private readonly Func<IReadOnlyList<string>> _urls;
    private readonly TimeZoneInfo _zone;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _cacheFor;
    private readonly Dictionary<string, (DateTime At, string Text)> _cache = [];
    private readonly object _gate = new();

    public CalendarSource(HttpClient http, Func<IReadOnlyList<string>> urls, TimeZoneInfo? zone = null, Func<DateTime>? utcNow = null, TimeSpan? cacheFor = null)
    {
        _http = http;
        _urls = urls;
        _zone = zone ?? TimeZoneInfo.Local;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _cacheFor = cacheFor ?? TimeSpan.FromMinutes(5);
    }

    public bool IsConfigured => _urls().Count > 0;

    /// <summary>Verilen yerel aralıkla kesişen etkinlikler, başlangıca göre sıralı. Ulaşılamayan takvimler <paramref name="errors"/>'a yazılır.</summary>
    public async Task<IReadOnlyList<CalendarItem>> GetAsync(DateTime fromLocal, DateTime toLocal, Action<string>? errors, CancellationToken ct)
    {
        var items = new List<CalendarItem>();
        foreach (var url in _urls())
        {
            try
            {
                var text = await DownloadAsync(url, ct);
                items.AddRange(Parse(text, fromLocal, toLocal));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                errors?.Invoke($"{Describe(url)}: {ex.Message}");
            }
        }

        return items.OrderBy(i => i.Start).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Adresin tamamı gizli olduğundan hata mesajlarında yalnızca sunucu adı gösterilir.</summary>
    internal static string Describe(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "takvim";

    private async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(url, out var hit) && _utcNow() - hit.At < _cacheFor)
            {
                return hit.Text;
            }
        }

        // webcal:// Apple/Outlook'un https için kullandığı şema.
        var address = url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[9..] : url;
        using var response = await _http.GetAsync(address, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"indirilemedi ({(int)response.StatusCode}); adres süresi dolmuş ya da yanlış olabilir");
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("takvim dosyası değil; ICS adresini kontrol et");
        }

        lock (_gate)
        {
            _cache[url] = (_utcNow(), text);
        }

        return text;
    }

    internal IReadOnlyList<CalendarItem> Parse(string ics, DateTime fromLocal, DateTime toLocal)
    {
        var calendar = IcalCalendar.Load(ics) ?? throw new InvalidDataException("takvim okunamadı");
        var from = ToUtc(fromLocal);
        var to = ToUtc(toLocal);
        var items = new List<CalendarItem>();

        // Gün başından önce başlayıp sürenler de görünsün diye aralığın başı birkaç gün geri alınır, sonra süzülür.
        var occurrences = calendar.GetOccurrences(new CalDateTime(from.AddDays(-3).Year, from.AddDays(-3).Month, from.AddDays(-3).Day, 0, 0, 0, "UTC"))
            .TakeWhileBefore(new CalDateTime(to.Year, to.Month, to.Day, to.Hour, to.Minute, to.Second, "UTC"));
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Source is not CalendarEvent source
                || string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var allDay = source.IsAllDay;
            var start = Local(occurrence.Period.StartTime, allDay);
            var end = Local(occurrence.Period.EffectiveEndTime ?? occurrence.Period.StartTime, allDay);
            if (end <= start)
            {
                end = allDay ? start.AddDays(1) : start;
            }

            if (start >= toLocal || (end <= fromLocal && start < fromLocal))
            {
                continue;
            }

            var location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location.Trim();
            items.Add(new CalendarItem(
                string.IsNullOrWhiteSpace(source.Summary) ? "(başlıksız)" : source.Summary.Trim(),
                start, end, allDay, location, FindMeetingUrl(location, source.Description)));
        }

        return items;
    }

    private DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

    private DateTime Local(CalDateTime value, bool allDay)
    {
        // Tüm gün ve "kayan" (saat dilimsiz) değerler olduğu gibi yerel kabul edilir; AsUtc onlarda yanlış sonuç verir.
        if (allDay || value.IsFloating)
        {
            return DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, allDay ? 0 : value.Hour, allDay ? 0 : value.Minute, allDay ? 0 : value.Second), DateTimeKind.Unspecified);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc), _zone);
    }

    internal static string? FindMeetingUrl(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (text is not null && MeetingUrlPattern().Match(text) is { Success: true } m)
            {
                return m.Value.TrimEnd('.', ',', ')', ';', '>', '\\');
            }
        }

        return null;
    }

    [GeneratedRegex(@"https?://(?:meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|(?:[\w-]+\.)?zoom\.us|[\w-]+\.webex\.com)/[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingUrlPattern();
}
