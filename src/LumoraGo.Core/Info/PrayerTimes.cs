using System.Globalization;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Info;

/// <param name="Times">Sırasıyla imsak, güneş, öğle, ikindi, akşam, yatsı.</param>
public sealed record PrayerDay(string City, DateOnly Date, IReadOnlyList<(string Name, TimeOnly Time)> Times);

/// <summary>
/// Namaz vakitleri: aladhan.com, Diyanet hesap yöntemiyle (method=13). Günün vakitleri gün boyu önbellekte kalır.
/// </summary>
public sealed class PrayerTimesService(HttpClient http)
{
    public static readonly string[] Names = ["İmsak", "Güneş", "Öğle", "İkindi", "Akşam", "Yatsı"];
    private static readonly string[] Keys = ["Imsak", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha"];

    private readonly Dictionary<(string, DateOnly), PrayerDay?> _cache = [];
    private readonly object _gate = new();

    /// <returns>Şehir bulunamazsa null.</returns>
    public async Task<PrayerDay?> GetAsync(string city, DateOnly date, CancellationToken ct)
    {
        var key = (FuzzyMatcher.Fold(city), date);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var url = $"https://api.aladhan.com/v1/timingsByCity/{date:dd-MM-yyyy}?city={Uri.EscapeDataString(Ascii(city))}&country=Turkey&method=13";
        using var doc = await InfoHttp.GetJsonAsync(http, url, ct).ConfigureAwait(false);
        var day = doc is null ? null : Parse(doc.RootElement.Obj("data"), Display(city), date);
        lock (_gate)
        {
            _cache[key] = day;
        }

        return day;
    }

    internal static PrayerDay? Parse(System.Text.Json.JsonElement data, string city, DateOnly date)
    {
        var timings = data.Obj("timings");
        var times = new List<(string, TimeOnly)>();
        for (var i = 0; i < Keys.Length; i++)
        {
            // "05:39 (+03)" gibi de gelebilir.
            var text = timings.Str(Keys[i])?.Split(' ')[0];
            if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return null;
            }

            times.Add((Names[i], time));
        }

        return new PrayerDay(city, date, times);
    }

    /// <summary>aladhan Türkçe harfleri tanımıyor: "İzmir" → "Izmir", "Muğla" → "Mugla".</summary>
    internal static string Ascii(string city)
    {
        var map = new Dictionary<char, char>
        {
            ['ı'] = 'i', ['İ'] = 'I', ['ğ'] = 'g', ['Ğ'] = 'G', ['ü'] = 'u', ['Ü'] = 'U',
            ['ş'] = 's', ['Ş'] = 'S', ['ö'] = 'o', ['Ö'] = 'O', ['ç'] = 'c', ['Ç'] = 'C', ['â'] = 'a', ['î'] = 'i', ['û'] = 'u',
        };
        return new string(city.Trim().Select(c => map.TryGetValue(c, out var a) ? a : c).ToArray());
    }

    private static string Display(string city)
    {
        var text = city.Trim();
        return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("tr-TR")) + text[1..];
    }
}
