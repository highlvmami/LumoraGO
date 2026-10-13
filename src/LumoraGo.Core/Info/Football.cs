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
public sealed class FootballService(HttpClient http, Func<DateTime>? now = null)
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

    /// <summary>Takım aranırken bugünden bu kadar geriye ve ileriye bakılır.</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromDays(21);

    public static readonly TimeSpan LookAhead = TimeSpan.FromDays(28);

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["fb"] = "fenerbahce",
        ["gs"] = "galatasaray",
        ["bjk"] = "besiktas",
        ["ts"] = "trabzonspor",
    };

    /// <summary>
    /// Takım adı boşsa yalnızca Süper Lig'in güncel haftası. Doluysa tüm liglerde o takımın son haftalardaki ve
    /// önümüzdeki haftalardaki maçları: önce canlı, sonra son oynanan, sonra sıradaki.
    /// </summary>
    public async Task<IReadOnlyList<Match>> FindAsync(string team, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(team.Trim());
        folded = Aliases.GetValueOrDefault(folded, folded);
        if (folded.Length == 0)
        {
            return (await BoardAsync(Leagues[0], "", ct)).OrderBy(m => m.State == MatchState.Live ? 0 : 1).ThenBy(m => m.StartUtc).ToList();
        }

        // Tarihsiz skor tablosu yalnızca içinde bulunulan haftayı verir; takımın o hafta maçı yoksa hiçbir şey çıkmaz.
        var today = (now ?? (() => DateTime.UtcNow))().Date;
        var range = $"?dates={(today - LookBack):yyyyMMdd}-{(today + LookAhead):yyyyMMdd}&limit=500";
        var boards = await Task.WhenAll(Leagues.Select(l => BoardAsync(l, range, ct)));
        var mine = boards.SelectMany(b => b)
            .Where(m => Matches(folded, m.Home) || Matches(folded, m.Away))
            .DistinctBy(m => (m.Home, m.Away, m.StartUtc))
            .ToList();

        var live = mine.Where(m => m.State == MatchState.Live).OrderBy(m => m.StartUtc);
        var past = mine.Where(m => m.State == MatchState.Finished).OrderByDescending(m => m.StartUtc).Take(4).ToList();
        var next = mine.Where(m => m.State == MatchState.Upcoming).OrderBy(m => m.StartUtc).Take(4).ToList();
        return [.. live, .. past.Take(1), .. next.Take(1), .. next.Skip(1), .. past.Skip(1)];
    }

    private async Task<List<Match>> BoardAsync((string Code, string Name) league, string query, CancellationToken ct)
    {
        try
        {
            using var json = await InfoHttp.GetJsonAsync(http, $"https://site.api.espn.com/apis/site/v2/sports/soccer/{league.Code}/scoreboard{query}", ct);
            return json is null ? [] : Parse(league.Name, json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Bir lig yanıt vermezse diğerlerinin sonuçları yine gösterilir.
            return [];
        }
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
