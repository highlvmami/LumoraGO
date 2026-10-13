using System.Globalization;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/saat tokyo": şehirlerdeki yerel saat ve farkı. Enter saati kopyalar.</summary>
public sealed class WorldClockPlugin(Func<DateTimeOffset>? utcNow = null) : IPlugin
{
    public const string Keyword = "/saat";
    public static readonly PluginMetadata Metadata = new("lumorago.clock", "Dünya saati", Keyword);

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    internal static readonly (string City, string Zone, string Words)[] Cities =
    [
        ("İstanbul", "Europe/Istanbul", "turkiye ankara izmir"),
        ("Londra", "Europe/London", "ingiltere london uk"),
        ("Berlin", "Europe/Berlin", "almanya germany"),
        ("Paris", "Europe/Paris", "fransa france"),
        ("Amsterdam", "Europe/Amsterdam", "hollanda netherlands"),
        ("Madrid", "Europe/Madrid", "ispanya spain"),
        ("Roma", "Europe/Rome", "italya italy rome"),
        ("Atina", "Europe/Athens", "yunanistan greece athens"),
        ("Moskova", "Europe/Moscow", "rusya russia moscow"),
        ("Kiev", "Europe/Kyiv", "ukrayna ukraine kyiv"),
        ("Bakü", "Asia/Baku", "azerbaycan baku"),
        ("Tahran", "Asia/Tehran", "iran tehran"),
        ("Dubai", "Asia/Dubai", "bae emirlikler uae"),
        ("Riyad", "Asia/Riyadh", "suudi arabistan riyadh"),
        ("Karaçi", "Asia/Karachi", "pakistan karachi"),
        ("Yeni Delhi", "Asia/Kolkata", "hindistan india delhi"),
        ("Bangkok", "Asia/Bangkok", "tayland thailand"),
        ("Singapur", "Asia/Singapore", "singapore"),
        ("Pekin", "Asia/Shanghai", "cin china beijing sanghay"),
        ("Hong Kong", "Asia/Hong_Kong", "hongkong"),
        ("Seul", "Asia/Seoul", "kore korea seoul"),
        ("Tokyo", "Asia/Tokyo", "japonya japan"),
        ("Sidney", "Australia/Sydney", "avustralya australia sydney"),
        ("Auckland", "Pacific/Auckland", "yeni zelanda new zealand"),
        ("New York", "America/New_York", "abd amerika usa newyork washington"),
        ("Toronto", "America/Toronto", "kanada canada"),
        ("Şikago", "America/Chicago", "chicago"),
        ("Los Angeles", "America/Los_Angeles", "kaliforniya california san francisco"),
        ("Meksiko", "America/Mexico_City", "meksika mexico"),
        ("Sao Paulo", "America/Sao_Paulo", "brezilya brazil"),
        ("Buenos Aires", "America/Argentina/Buenos_Aires", "arjantin argentina"),
        ("Kahire", "Africa/Cairo", "misir egypt cairo"),
        ("Lagos", "Africa/Lagos", "nijerya nigeria"),
        ("Johannesburg", "Africa/Johannesburg", "guney afrika south africa"),
    ];

    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var now = utcNow?.Invoke() ?? DateTimeOffset.UtcNow;
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(now);
        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        var results = new List<Result>();

        foreach (var (city, zoneId, words) in Cities)
        {
            // Kelime başından eşleşme; "new york" gibi boşluklu aramalar da çalışsın.
            if (folded.Length > 0 && !(" " + FuzzyMatcher.Fold(city) + " " + words).Contains(" " + folded, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryZone(zoneId, out var zone))
            {
                continue;
            }

            var there = TimeZoneInfo.ConvertTime(now, zone);
            var time = there.ToString("HH:mm", Turkish);
            var subtitle = $"{there.ToString("d MMMM dddd", Turkish)} · {Difference(there.Offset - localOffset)}";
            results.Add(new Result($"{city}: {time}", subtitle, null, 100 - results.Count, _ =>
            {
                _host?.CopyToClipboard($"{city} {time}");
                return true;
            }));
        }

        if (results.Count == 0)
        {
            results.Add(new Result("Şehir bulunamadı", "Örnek: /saat tokyo, /saat londra, /saat new york", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static string Difference(TimeSpan diff)
    {
        if (diff == TimeSpan.Zero)
        {
            return "seninle aynı saat";
        }

        var hours = Math.Abs(diff.TotalHours);
        var text = hours % 1 == 0 ? $"{hours:0} saat" : $"{hours.ToString("0.#", Turkish)} saat";
        return diff > TimeSpan.Zero ? $"{text} ileride" : $"{text} geride";
    }

    private static bool TryZone(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
