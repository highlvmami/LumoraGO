using System.Globalization;
using System.Text.Json;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Info;

public enum MatchState
{
    Upcoming,
    Live,
    Finished,
}

public sealed record Match(string League, string Home, string Away, int? HomeScore, int? AwayScore, MatchState State, string Status, DateTime StartUtc);

/// <param name="Rank">Sıra.</param>
/// <param name="Difference">Averaj, işaretiyle ("+5").</param>
public sealed record Standing(int Rank, string Team, int Played, int Won, int Drawn, int Lost, string Difference, int Points);

/// <summary>
/// Maç skorları ve puan tablosu için ESPN'in herkese açık (resmi olmayan) adresleri.
/// Takım aranınca önce o ligin takım listesinden kimliği bulunur, sonra takımın oynanmış ve oynanacak maçları istenir.
/// (ESPN skor tablosu tarih aralığı kabul etmiyor, tarihsiz hali de yalnızca sıradaki maç gününü veriyor.)
/// </summary>
public sealed class FootballService(HttpClient http, Func<DateTime>? now = null)
{
    private const string Site = "https://site.api.espn.com/apis/site/v2/sports/soccer";
    private const string Standings = "https://site.api.espn.com/apis/v2/sports/soccer";

    public static readonly (string Code, string Name)[] Leagues =
    [
        ("tur.1", "Süper Lig"),
        ("eng.1", "Premier Lig"),
        ("esp.1", "La Liga"),
        ("ita.1", "Serie A"),
        ("ger.1", "Bundesliga"),
        ("fra.1", "Ligue 1"),
    ];

    /// <summary>"/lig premier" gibi yazımlar için lig adları.</summary>
    private static readonly Dictionary<string, string> LeagueNames = new()
    {
        ["super"] = "tur.1", ["superlig"] = "tur.1", ["turkiye"] = "tur.1",
        ["premier"] = "eng.1", ["ingiltere"] = "eng.1",
        ["laliga"] = "esp.1", ["ispanya"] = "esp.1",
        ["serie"] = "ita.1", ["seriea"] = "ita.1", ["italya"] = "ita.1",
        ["bundesliga"] = "ger.1", ["almanya"] = "ger.1",
        ["ligue"] = "fra.1", ["ligue1"] = "fra.1", ["fransa"] = "fra.1",
    };

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["fb"] = "fenerbahce",
        ["fener"] = "fenerbahce",
        ["gs"] = "galatasaray",
        ["cimbom"] = "galatasaray",
        ["bjk"] = "besiktas",
        ["ts"] = "trabzonspor",
    };

    /// <summary>ESPN Türk takımlarını Türkçe harfsiz yazıyor; ekranda doğrusu görünsün.</summary>
    private static readonly Dictionary<string, string> TurkishNames = new()
    {
        ["Fenerbahce"] = "Fenerbahçe",
        ["Besiktas"] = "Beşiktaş",
        ["Genclerbirligi"] = "Gençlerbirliği",
        ["Goztepe"] = "Göztepe",
        ["Kasimpasa"] = "Kasımpaşa",
        ["Istanbul Basaksehir"] = "İstanbul Başakşehir",
        ["Eyupspor"] = "Eyüpspor",
        ["Caykur Rizespor"] = "Çaykur Rizespor",
        ["Fatih Karagumruk"] = "Fatih Karagümrük",
    };

    private static readonly TimeSpan TeamListFor = TimeSpan.FromHours(12);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime At, List<(string Id, string Name, string[] Keys)> Teams)> _teams = [];

    public static string Pretty(string name) => TurkishNames.GetValueOrDefault(name, name);

    /// <summary>
    /// Takım adı boşsa Süper Lig'in bu haftası. Doluysa takımın maçları: canlı varsa o, sonra son oynanan maç,
    /// sonra sıradaki maçlar, en altta önceki maçlar.
    /// </summary>
    public async Task<IReadOnlyList<Match>> FindAsync(string team, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(team.Trim());
        folded = Aliases.GetValueOrDefault(folded, folded);
        return folded.Length == 0 ? await WeekAsync(ct).ConfigureAwait(false) : await TeamAsync(folded, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Match>> TeamAsync(string folded, CancellationToken ct)
    {
        foreach (var league in Leagues)
        {
            var teams = await TeamsAsync(league.Code, ct).ConfigureAwait(false);
            var found = teams.FirstOrDefault(t => t.Keys.Contains(folded));
            if (found.Id is null)
            {
                found = teams.FirstOrDefault(t => t.Keys.Any(k => k.Split(' ').Any(w => w.StartsWith(folded, StringComparison.Ordinal))));
            }

            if (found.Id is null)
            {
                continue;
            }

            var schedules = await Task.WhenAll(
                ScheduleAsync(league, found.Id, "", ct),
                ScheduleAsync(league, found.Id, "?fixture=true", ct)).ConfigureAwait(false);
            var all = schedules.SelectMany(s => s).DistinctBy(m => (m.Home, m.Away, m.StartUtc)).ToList();

            var live = all.Where(m => m.State == MatchState.Live).OrderBy(m => m.StartUtc);
            var past = all.Where(m => m.State == MatchState.Finished).OrderByDescending(m => m.StartUtc).Take(5).ToList();
            var next = all.Where(m => m.State == MatchState.Upcoming).OrderBy(m => m.StartUtc).Take(5).ToList();
            return [.. live, .. past.Take(1), .. next, .. past.Skip(1)];
        }

        return [];
    }

    private async Task<IReadOnlyList<Match>> WeekAsync(CancellationToken ct)
    {
        var today = _now().Date;
        var months = new[] { today.AddDays(-3), today.AddDays(4) }.Select(d => d.ToString("yyyyMM", CultureInfo.InvariantCulture)).Distinct();
        var boards = await Task.WhenAll(months.Select(m => GetAsync(Leagues[0].Name, $"{Site}/{Leagues[0].Code}/scoreboard?dates={m}", ct))).ConfigureAwait(false);
        var all = boards.SelectMany(b => b).DistinctBy(m => (m.Home, m.Away, m.StartUtc)).ToList();
        var week = all.Where(m => m.StartUtc >= today.AddDays(-3) && m.StartUtc < today.AddDays(5)).ToList();
        if (week.Count == 0)
        {
            week = all.Where(m => m.State != MatchState.Finished).Take(10).ToList();
        }

        return week.OrderBy(m => m.State == MatchState.Live ? 0 : 1).ThenBy(m => m.StartUtc).ToList();
    }

    private async Task<List<(string Id, string Name, string[] Keys)>> TeamsAsync(string league, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_teams.TryGetValue(league, out var hit) && _now() - hit.At < TeamListFor)
            {
                return hit.Teams;
            }
        }

        List<(string Id, string Name, string[] Keys)> teams;
        try
        {
            using var json = await InfoHttp.GetJsonAsync(http, $"{Site}/{league}/teams", ct).ConfigureAwait(false);
            teams = json is null ? [] : ParseTeams(json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return [];
        }

        lock (_gate)
        {
            _teams[league] = (_now(), teams);
        }

        return teams;
    }

    internal static List<(string Id, string Name, string[] Keys)> ParseTeams(JsonElement root) =>
        root.Arr("sports").SelectMany(s => s.Arr("leagues")).SelectMany(l => l.Arr("teams"))
            .Select(t => t.Obj("team"))
            .Where(t => t.Str("id") is not null)
            .Select(t =>
            {
                var name = t.Str("displayName") ?? "?";
                var keys = new[] { name, t.Str("shortDisplayName"), t.Str("abbreviation"), t.Str("location"), t.Str("name") }
                    .OfType<string>()
                    .Select(FuzzyMatcher.Fold)
                    .Distinct()
                    .ToArray();
                return (t.Str("id")!, name, keys);
            })
            .ToList();

    private Task<List<Match>> ScheduleAsync((string Code, string Name) league, string teamId, string query, CancellationToken ct) =>
        GetAsync(league.Name, $"{Site}/{league.Code}/teams/{teamId}/schedule{query}", ct);

    private async Task<List<Match>> GetAsync(string league, string url, CancellationToken ct)
    {
        try
        {
            using var json = await InfoHttp.GetJsonAsync(http, url, ct).ConfigureAwait(false);
            return json is null ? [] : Parse(league, json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Bir istek başarısız olursa diğerlerinin sonuçları yine gösterilir.
            return [];
        }
    }

    /// <summary>"/lig" için lig kodu; boşsa Süper Lig, tanınmazsa null.</summary>
    public static (string Code, string Name)? FindLeague(string text)
    {
        var folded = FuzzyMatcher.Fold(text.Trim()).Replace(" ", "", StringComparison.Ordinal);
        if (folded.Length == 0)
        {
            return Leagues[0];
        }

        var code = LeagueNames.GetValueOrDefault(folded)
            ?? LeagueNames.Where(p => p.Key.StartsWith(folded, StringComparison.Ordinal)).Select(p => p.Value).FirstOrDefault();
        return code is null ? null : Leagues.First(l => l.Code == code);
    }

    /// <summary>Lig puan tablosu.</summary>
    public async Task<IReadOnlyList<Standing>> TableAsync(string leagueCode, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http, $"{Standings}/{leagueCode}/standings", ct).ConfigureAwait(false);
        return json is null ? [] : ParseStandings(json.RootElement);
    }

    internal static List<Standing> ParseStandings(JsonElement root)
    {
        var entries = root.Arr("children").SelectMany(c => c.Obj("standings").Arr("entries")).ToList();
        if (entries.Count == 0)
        {
            entries = root.Obj("standings").Arr("entries").ToList();
        }

        return entries.Select(e =>
            {
                var stats = e.Arr("stats").Where(s => s.Str("name") is not null).GroupBy(s => s.Str("name")!).ToDictionary(g => g.Key, g => g.First());
                int Stat(string name) => stats.TryGetValue(name, out var s) && s.Num("value") is { } v ? (int)v : 0;
                var diff = stats.TryGetValue("pointDifferential", out var d) ? d.Str("displayValue") ?? "0" : "0";
                return new Standing(Stat("rank"), Pretty(e.Obj("team").Str("displayName") ?? "?"), Stat("gamesPlayed"), Stat("wins"), Stat("ties"), Stat("losses"), diff, Stat("points"));
            })
            .OrderBy(s => s.Rank == 0 ? int.MaxValue : s.Rank)
            .ThenByDescending(s => s.Points)
            .ToList();
    }

    /// <summary>Hem skor tablosunu hem takım fikstürünü okur (ikisinde durum ve skor farklı yerde).</summary>
    internal static List<Match> Parse(string league, JsonElement root)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var matches = new List<Match>();
        foreach (var e in root.Arr("events"))
        {
            var competition = e.Arr("competitions").FirstOrDefault();
            var teams = competition.Arr("competitors").ToList();
            var home = teams.FirstOrDefault(t => t.Str("homeAway") == "home");
            var away = teams.FirstOrDefault(t => t.Str("homeAway") == "away");
            if (home.ValueKind != JsonValueKind.Object || away.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var statusElement = e.Obj("status").ValueKind == JsonValueKind.Object ? e.Obj("status") : competition.Obj("status");
            var state = statusElement.Obj("type").Str("state") switch
            {
                "in" => MatchState.Live,
                "post" => MatchState.Finished,
                _ => MatchState.Upcoming,
            };
            var start = DateTime.TryParse(e.Str("date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
            var status = state switch
            {
                MatchState.Live => statusElement.Str("displayClock") is { Length: > 0 } clock ? $"Canlı {clock}" : "Canlı",
                MatchState.Finished => start == DateTime.MinValue ? "Bitti" : "Bitti · " + start.ToLocalTime().ToString("d MMM ddd", tr),
                _ => start == DateTime.MinValue ? "Başlamadı" : start.ToLocalTime().ToString("d MMM ddd HH:mm", tr),
            };

            matches.Add(new Match(
                league,
                Name(home),
                Name(away),
                state == MatchState.Upcoming ? null : Score(home),
                state == MatchState.Upcoming ? null : Score(away),
                state,
                status,
                start));
        }

        return matches;
    }

    private static string Name(JsonElement competitor) =>
        Pretty(competitor.Obj("team").Str("displayName") ?? competitor.Obj("team").Str("shortDisplayName") ?? "?");

    /// <summary>Skor tablosunda "2", fikstürde {"value":2.0,"displayValue":"2"}.</summary>
    private static int? Score(JsonElement competitor)
    {
        var score = competitor.Obj("score");
        var text = score.ValueKind switch
        {
            JsonValueKind.String => score.GetString(),
            JsonValueKind.Number => score.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Object => score.Str("displayValue") ?? score.Num("value")?.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        return text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
