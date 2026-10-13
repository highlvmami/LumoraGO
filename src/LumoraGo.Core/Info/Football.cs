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

/// <summary>
/// Maç skorları için ESPN'in herkese açık (resmi olmayan) skor tablosu. Süper Lig, büyük Avrupa ligleri ve UEFA kupaları taranır.
/// </summary>
public sealed class FootballService(HttpClient http)
{
    public static readonly (string Code, string Name)[] Leagues =
    [
        ("tur.1", "Süper Lig"),
        ("uefa.champions", "Şampiyonlar Ligi"),
        ("uefa.europa", "Avrupa Ligi"),
        ("uefa.europa.conf", "Konferans Ligi"),
        ("eng.1", "Premier Lig"),
        ("esp.1", "La Liga"),
        ("ita.1", "Serie A"),
        ("ger.1", "Bundesliga"),
        ("fra.1", "Ligue 1"),
    ];

    /// <summary>Takım adı boşsa yalnızca Süper Lig'in güncel haftası; doluysa tüm liglerde o takımın maçları.</summary>
    public async Task<IReadOnlyList<Match>> FindAsync(string team, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(team.Trim());
        var leagues = folded.Length == 0 ? Leagues.Take(1) : Leagues;
        var boards = await Task.WhenAll(leagues.Select(async l =>
        {
            try
            {
                using var json = await InfoHttp.GetJsonAsync(http, $"https://site.api.espn.com/apis/site/v2/sports/soccer/{l.Code}/scoreboard", ct);
                return json is null ? [] : Parse(l.Name, json.RootElement);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                return [];
            }
        }));

        return boards.SelectMany(b => b)
            .Where(m => folded.Length == 0 || Matches(folded, m.Home) || Matches(folded, m.Away))
            .OrderBy(m => m.State == MatchState.Live ? 0 : 1)
            .ThenBy(m => m.StartUtc)
            .ToList();
    }

    private static bool Matches(string folded, string team) =>
        FuzzyMatcher.Fold(team).Contains(folded, StringComparison.Ordinal)
        || folded.Contains(FuzzyMatcher.Fold(team), StringComparison.Ordinal);

    internal static List<Match> Parse(string league, JsonElement root)
    {
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

            var statusType = e.Obj("status").Obj("type");
            var state = statusType.Str("state") switch
            {
                "in" => MatchState.Live,
                "post" => MatchState.Finished,
                _ => MatchState.Upcoming,
            };
            var start = DateTime.TryParse(e.Str("date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
            var status = state switch
            {
                MatchState.Live => e.Obj("status").Str("displayClock") is { Length: > 0 } clock ? $"Canlı {clock}" : "Canlı",
                MatchState.Finished => "Bitti",
                _ => start == DateTime.MinValue ? "Başlamadı" : start.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.GetCultureInfo("tr-TR")),
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
        competitor.Obj("team").Str("displayName") ?? competitor.Obj("team").Str("shortDisplayName") ?? "?";

    private static int? Score(JsonElement competitor) =>
        competitor.Str("score") is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
