using System.Globalization;
using System.Text.RegularExpressions;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Actions;

/// <param name="When">Yerel saat.</param>
/// <param name="HasTime">Saat yazıldıysa (ya da "2 saat sonra" gibi göreliyse) true; yalnızca gün yazıldıysa saat 09:00 olur.</param>
/// <param name="Shifted">Yazılan saat bugün geçmiş olduğu için ertesi güne kaydırıldı.</param>
/// <param name="Rest">Tarih kelimelerinden sonra kalan metin ("fatura").</param>
public sealed record DateMatch(DateTime When, bool HasTime, bool Shifted, string Rest)
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"Yarın 14 Ekim Salı 09:00" gibi okunur biçim.</summary>
    public string Describe(DateTime now)
    {
        var day = (When.Date - now.Date).Days switch
        {
            0 => "Bugün ",
            1 => "Yarın ",
            _ => "",
        };
        var text = day + When.ToString(When.Year == now.Year ? "d MMMM dddd" : "d MMMM yyyy dddd", Tr);
        return HasTime ? $"{text} {When:HH\\:mm}" : text;
    }
}

/// <summary>
/// Kutuya yazılan Türkçe tarih ve saatleri okur: bugün, yarın, öbür gün, haftaya, pazartesi…pazar, "cuma 14:30",
/// "3 kasım", "2 saat sonra", "20 dk sonra", sabah (09:00), öğlen (12:00), akşam (19:00), gece (22:00).
/// Tarih kelimeleri metnin başında olmalı; okunamayan ilk kelimede durulur ve gerisi <see cref="DateMatch.Rest"/> olur.
/// </summary>
public static partial class TurkishDate
{
    private static readonly string[] Weekdays = ["pazar", "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi"];

    private static readonly string[] Months = ["ocak", "subat", "mart", "nisan", "mayis", "haziran", "temmuz", "agustos", "eylul", "ekim", "kasim", "aralik"];

    private static readonly Dictionary<string, int> PartsOfDay = new()
    {
        ["sabah"] = 9,
        ["ogle"] = 12,
        ["oglen"] = 12,
        ["ogleden"] = 14,
        ["aksam"] = 19,
        ["gece"] = 22,
    };

    public static readonly TimeSpan DefaultTime = TimeSpan.FromHours(9);

    [GeneratedRegex(@"^(\d{1,2})(?:[:.](\d{2}))?(?:'?(?:da|de|ta|te))?$")]
    private static partial Regex TimeToken();

    /// <summary>Metnin başında tarih ya da saat yoksa null.</summary>
    public static DateMatch? Parse(string text, DateTime now)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var folded = words.Select(w => FuzzyMatcher.Fold(w).Replace('’', '\'').TrimEnd(',')).ToArray();

        DateTime? day = null;
        TimeSpan? time = null;
        DateTime? exact = null;
        int? partHour = null;
        var nextWeek = false;
        var weekday = false;
        var i = 0;

        while (i < words.Length)
        {
            var w = folded[i];
            var next = i + 1 < folded.Length ? folded[i + 1] : "";
            var after = i + 2 < folded.Length ? folded[i + 2] : "";

            if (w == "bugun")
            {
                day = now.Date;
                i++;
            }
            else if (w is "yarin")
            {
                day = now.Date.AddDays(1);
                i++;
            }
            else if (w is "oburgun" or "ertesigun")
            {
                day = now.Date.AddDays(2);
                i++;
            }
            else if (w is "obur" or "ertesi" && next == "gun")
            {
                day = now.Date.AddDays(2);
                i += 2;
            }
            else if (w is "haftaya" or "gelecek" && (w == "haftaya" || next == "hafta"))
            {
                nextWeek = true;
                day ??= now.Date.AddDays(7);
                i += w == "haftaya" ? 1 : 2;
            }
            else if (Array.IndexOf(Weekdays, w) is var wd && wd >= 0)
            {
                var offset = ((wd - (int)now.DayOfWeek) + 7) % 7;
                day = now.Date.AddDays(nextWeek ? (offset == 0 ? 7 : offset + 7) : offset);
                weekday = true;
                i++;
            }
            else if (int.TryParse(w, out var n) && n > 0 && Array.IndexOf(Months, next) is var month && month >= 0)
            {
                var year = now.Year;
                if (n > DateTime.DaysInMonth(year, month + 1))
                {
                    break;
                }

                var date = new DateTime(year, month + 1, n);
                if (date < now.Date)
                {
                    date = date.AddYears(1);
                }

                day = date;
                i += 2;
            }
            else if (int.TryParse(w, out var amount) && amount > 0 && after == "sonra" && Unit(next) is { } unit)
            {
                exact = now + unit * amount;
                i += 3;
            }
            else if (PartsOfDay.TryGetValue(w, out var hour))
            {
                partHour = hour;
                i++;
                if (w == "ogleden" && next == "sonra")
                {
                    i++;
                }
            }
            else if (w == "saat" && TimeToken().Match(next) is { Success: true } && ParseTime(next) is { } t1)
            {
                time = t1;
                i += 2;
            }
            else if (TimeToken().Match(w) is { Success: true } && ParseTime(w) is { } t2
                && (w.Contains(':') || w.Contains('.') || w.Contains('\'') || day is not null || partHour is not null || w.Length > 2 && !char.IsAsciiDigit(w[^1])))
            {
                // Çıplak "9" ancak bir günden ya da "akşam"dan sonra saat sayılır; yoksa "3 elma" saate dönüşürdü.
                time = t2;
                i++;
            }
            else
            {
                break;
            }
        }

        if (i == 0)
        {
            return null;
        }

        var rest = string.Join(' ', words.Skip(i));
        if (exact is { } at)
        {
            return new DateMatch(new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0), true, false, rest);
        }

        if (partHour is { } ph)
        {
            if (time is { } given && given.Hours < 12 && ph >= 12)
            {
                time = given.Add(TimeSpan.FromHours(12));
            }
            else
            {
                time ??= TimeSpan.FromHours(ph);
            }
        }

        var hasTime = time is not null;
        var when = (day ?? now.Date) + (time ?? DefaultTime);
        var shifted = false;
        if (when <= now)
        {
            if (weekday && day == now.Date)
            {
                // "salı 9" salı öğleden sonra yazılırsa gelecek salı demektir.
                when = when.AddDays(7);
            }
            else if (day is null || day == now.Date)
            {
                when = when.AddDays(1);
                shifted = true;
            }
        }

        return new DateMatch(when, hasTime, shifted, rest);
    }

    private static TimeSpan? Unit(string word) => word switch
    {
        "dk" or "dak" or "dakika" => TimeSpan.FromMinutes(1),
        "sa" or "saat" => TimeSpan.FromHours(1),
        "gun" => TimeSpan.FromDays(1),
        "hafta" => TimeSpan.FromDays(7),
        _ => null,
    };

    private static TimeSpan? ParseTime(string token)
    {
        var m = TimeToken().Match(token);
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? new TimeSpan(hour, minute, 0) : null;
    }
}
