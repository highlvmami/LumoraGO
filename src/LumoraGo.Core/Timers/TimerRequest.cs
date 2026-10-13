using System.Globalization;
using System.Text.RegularExpressions;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Timers;

/// <summary>"10dk çay", "1sa 30dk toplantı", "90 sn", "14:30 ilaç" gibi zamanlayıcı yazımlarını okur.</summary>
public static partial class TimerRequest
{
    /// <summary>Yanlışlıkla haftalarca süren zamanlayıcı kurulmasın.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(1);

    [GeneratedRegex(@"^(?<n>\d+(?:[.,]\d+)?)\s*(?<u>[a-z]+)$")]
    private static partial Regex PartPattern();

    [GeneratedRegex(@"^(?<h>[01]?\d|2[0-3])[:.](?<m>[0-5]\d)$")]
    private static partial Regex ClockPattern();

    private static readonly Dictionary<string, TimeSpan> Units = new(StringComparer.Ordinal)
    {
        ["sn"] = TimeSpan.FromSeconds(1), ["s"] = TimeSpan.FromSeconds(1), ["saniye"] = TimeSpan.FromSeconds(1),
        ["sec"] = TimeSpan.FromSeconds(1), ["second"] = TimeSpan.FromSeconds(1), ["seconds"] = TimeSpan.FromSeconds(1),
        ["dk"] = TimeSpan.FromMinutes(1), ["d"] = TimeSpan.FromMinutes(1), ["dakika"] = TimeSpan.FromMinutes(1),
        ["m"] = TimeSpan.FromMinutes(1), ["min"] = TimeSpan.FromMinutes(1), ["mins"] = TimeSpan.FromMinutes(1),
        ["minute"] = TimeSpan.FromMinutes(1), ["minutes"] = TimeSpan.FromMinutes(1),
        ["sa"] = TimeSpan.FromHours(1), ["saat"] = TimeSpan.FromHours(1), ["h"] = TimeSpan.FromHours(1),
        ["hr"] = TimeSpan.FromHours(1), ["hour"] = TimeSpan.FromHours(1), ["hours"] = TimeSpan.FromHours(1),
    };

    /// <summary>Başta bir süre (veya saat) ve ardından isteğe bağlı bir etiket bekler. Süre birimsizse dakika sayılır.</summary>
    /// <param name="now">"14:30" gibi saatlerde kalan süreyi hesaplamak için yerel saat.</param>
    public static bool TryParse(string text, DateTime now, out TimeSpan duration, out string label)
    {
        duration = TimeSpan.Zero;
        label = "";
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var clock = ClockPattern().Match(words[0]);
        if (clock.Success)
        {
            var at = now.Date.AddHours(int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture))
                .AddMinutes(int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture));
            if (at <= now)
            {
                at = at.AddDays(1);
            }

            duration = at - now;
            label = string.Join(' ', words.Skip(1));
            return true;
        }

        // "1sa 30dk" gibi birden fazla parça; "10 dk" gibi sayı ile birim ayrı yazılmış olabilir.
        var used = 0;
        while (used < words.Length)
        {
            var word = FuzzyMatcher.Fold(words[used]);
            var unitWord = used + 1 < words.Length ? FuzzyMatcher.Fold(words[used + 1]) : null;
            if (double.TryParse(word.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var bare)
                && unitWord is not null && Units.TryGetValue(unitWord, out var separateUnit))
            {
                duration += separateUnit * bare;
                used += 2;
                continue;
            }

            var part = PartPattern().Match(word);
            if (part.Success && Units.TryGetValue(part.Groups["u"].Value, out var unit))
            {
                duration += unit * double.Parse(part.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                used++;
                continue;
            }

            if (used == 0 && double.TryParse(word.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
            {
                duration = TimeSpan.FromMinutes(minutes);
                used = 1;
            }

            break;
        }

        if (used == 0 || duration < TimeSpan.FromSeconds(1) || duration > MaxDuration)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        duration = TimeSpan.FromSeconds(Math.Round(duration.TotalSeconds));
        label = string.Join(' ', words.Skip(used));
        return true;
    }

    /// <summary>"1 sa 5 dk", "10 dk", "45 sn" gibi kısa Türkçe süre.</summary>
    public static string Format(TimeSpan span)
    {
        var parts = new List<string>();
        var hours = (int)span.TotalHours;
        if (hours > 0)
        {
            parts.Add($"{hours} sa");
        }

        if (span.Minutes > 0)
        {
            parts.Add($"{span.Minutes} dk");
        }

        if (span.Seconds > 0 && hours == 0)
        {
            parts.Add($"{span.Seconds} sn");
        }

        return parts.Count == 0 ? "0 sn" : string.Join(' ', parts);
    }
}
