using System.Globalization;
using System.Text.Json;

namespace LumoraGo.Core.Info;

public sealed record Place(string Name, string? Region, string? Country, double Latitude, double Longitude)
{
    public string Label => Region is { Length: > 0 } r && !string.Equals(r, Name, StringComparison.OrdinalIgnoreCase)
        ? $"{Name}, {r}"
        : Country is { Length: > 0 } c ? $"{Name}, {c}" : Name;
}

/// <param name="Hourly">Şu andan itibaren saatlik sıcaklıklar (en çok 12).</param>
public sealed record WeatherDay(DateOnly Date, double Max, double Min, int? RainChance, int Code);

public sealed record WeatherReport(
    Place Place,
    double Temperature,
    double FeelsLike,
    double Wind,
    int Code,
    IReadOnlyList<WeatherDay> Days,
    IReadOnlyList<double> Hourly);

/// <summary>Open-Meteo (anahtarsız, ücretsiz) ile hava durumu; şehir adı Open-Meteo geocoding ile, şehir yoksa IP'den bulunur.</summary>
public sealed class WeatherService(HttpClient http)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<Place?> FindAsync(string city, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http,
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=tr&format=json", ct);
        var first = json?.RootElement.Arr("results").FirstOrDefault() ?? default;
        return first.ValueKind == JsonValueKind.Object && first.Num("latitude") is { } lat && first.Num("longitude") is { } lon
            ? new Place(first.Str("name") ?? city, first.Str("admin1"), first.Str("country"), lat, lon)
            : null;
    }

    /// <summary>Şehir ayarlanmamışsa dış IP'nin yaklaşık konumu (ipwho.is).</summary>
    public async Task<Place?> HereAsync(CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http, "https://ipwho.is/?fields=success,city,region,country,latitude,longitude", ct);
        var root = json?.RootElement ?? default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True
            && root.Num("latitude") is { } lat && root.Num("longitude") is { } lon
            ? new Place(root.Str("city") ?? "Bulunduğun yer", root.Str("region"), root.Str("country"), lat, lon)
            : null;
    }

    public async Task<WeatherReport> ForecastAsync(Place place, CancellationToken ct)
    {
        var url = "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={place.Latitude.ToString(Invariant)}&longitude={place.Longitude.ToString(Invariant)}"
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m&forecast_hours=12"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&timezone=auto&forecast_days=7";
        using var json = await InfoHttp.GetJsonAsync(http, url, ct) ?? throw new HttpRequestException("hava durumu bulunamadı");
        return Parse(place, json.RootElement);
    }

    internal static WeatherReport Parse(Place place, JsonElement root)
    {
        var current = root.Obj("current");
        var daily = root.Obj("daily");
        var dates = daily.Arr("time").Select(t => DateOnly.ParseExact(t.GetString()!, "yyyy-MM-dd", Invariant)).ToList();
        var max = Numbers(daily, "temperature_2m_max");
        var min = Numbers(daily, "temperature_2m_min");
        var rain = Numbers(daily, "precipitation_probability_max");
        var codes = Numbers(daily, "weather_code");
        var days = dates.Select((d, i) => new WeatherDay(
            d,
            At(max, i) ?? double.NaN,
            At(min, i) ?? double.NaN,
            At(rain, i) is { } r ? (int)Math.Round(r) : null,
            (int)(At(codes, i) ?? 0))).ToList();

        return new WeatherReport(
            place,
            current.Num("temperature_2m") ?? throw new InvalidDataException("sıcaklık yok"),
            current.Num("apparent_temperature") ?? current.Num("temperature_2m")!.Value,
            current.Num("wind_speed_10m") ?? 0,
            (int)(current.Num("weather_code") ?? 0),
            days,
            Numbers(root.Obj("hourly"), "temperature_2m").OfType<double>().Take(12).ToList());
    }

    private static List<double?> Numbers(JsonElement owner, string name) =>
        owner.Arr(name).Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToList();

    private static double? At(List<double?> list, int i) => i < list.Count ? list[i] : null;

    /// <summary>WMO hava kodunun Türkçe adı ve emojisi.</summary>
    public static (string Text, string Emoji) Describe(int code) => code switch
    {
        0 => ("Açık", "☀️"),
        1 => ("Çoğunlukla açık", "🌤️"),
        2 => ("Parçalı bulutlu", "⛅"),
        3 => ("Kapalı", "☁️"),
        45 or 48 => ("Sisli", "🌫️"),
        51 or 53 or 55 => ("Çisenti", "🌦️"),
        56 or 57 => ("Donan çisenti", "🌧️"),
        61 => ("Hafif yağmurlu", "🌦️"),
        63 => ("Yağmurlu", "🌧️"),
        65 => ("Şiddetli yağmur", "🌧️"),
        66 or 67 => ("Donan yağmur", "🌧️"),
        71 => ("Hafif kar", "🌨️"),
        73 => ("Kar yağışlı", "🌨️"),
        75 => ("Yoğun kar", "❄️"),
        77 => ("Kar taneleri", "🌨️"),
        80 => ("Hafif sağanak", "🌦️"),
        81 => ("Sağanak", "🌧️"),
        82 => ("Şiddetli sağanak", "⛈️"),
        85 or 86 => ("Kar sağanağı", "🌨️"),
        95 => ("Gök gürültülü fırtına", "⛈️"),
        96 or 99 => ("Dolulu fırtına", "⛈️"),
        _ => ("Bilinmiyor", "🌡️"),
    };
}
