using LumoraGo.Core.Info;
using LumoraGo.Core.Plugins;

namespace LumoraGo.Core.Tests;

/// <summary>ESPN'in gerçek yanıtlarının kısaltılmış halleri (13 Ekim 2026'da GitHub Actions'tan alındı).</summary>
public sealed class FootballTests
{
    private const string Teams = """
        {"sports":[{"leagues":[{"teams":[
          {"team":{"id":"436","displayName":"Fenerbahce","shortDisplayName":"Fenerbahce","abbreviation":"FEN","location":"Fenerbahce"}},
          {"team":{"id":"432","displayName":"Galatasaray","shortDisplayName":"Galatasaray","abbreviation":"GAL","location":"Galatasaray"}},
          {"team":{"id":"1895","displayName":"Besiktas","shortDisplayName":"Besiktas","abbreviation":"BES","location":"Besiktas"}},
          {"team":{"id":"7914","displayName":"Istanbul Basaksehir","shortDisplayName":"Istanbul BB","abbreviation":"IBFK","location":"Istanbul Basaksehir"}}]}]}]}
        """;

    // Takım fikstüründe durum yarışmanın içinde, skor ise nesne.
    private const string Results = """
        {"events":[
          {"date":"2026-09-20T14:00Z","competitions":[{"status":{"type":{"state":"post"}},"competitors":[
            {"homeAway":"home","team":{"displayName":"Fenerbahce"},"score":{"value":8.0,"displayValue":"8"}},
            {"homeAway":"away","team":{"displayName":"Eyupspor"},"score":{"value":0.0,"displayValue":"0"}}]}]},
          {"date":"2026-10-10T16:00Z","competitions":[{"status":{"type":{"state":"post"}},"competitors":[
            {"homeAway":"home","team":{"displayName":"Caykur Rizespor"},"score":{"value":0.0,"displayValue":"0"}},
            {"homeAway":"away","team":{"displayName":"Fenerbahce"},"score":{"value":3.0,"displayValue":"3"}}]}]},
          {"date":"2026-09-14T17:00Z","competitions":[{"status":{"type":{"state":"post"}},"competitors":[
            {"homeAway":"home","team":{"displayName":"Gaziantep FK"},"score":{"value":0.0,"displayValue":"0"}},
            {"homeAway":"away","team":{"displayName":"Fenerbahce"},"score":{"value":0.0,"displayValue":"0"}}]}]}]}
        """;

    private const string Fixtures = """
        {"events":[
          {"date":"2026-10-26T18:30Z","competitions":[{"status":{"type":{"state":"pre"}},"competitors":[
            {"homeAway":"home","team":{"displayName":"Galatasaray"}},{"homeAway":"away","team":{"displayName":"Fenerbahce"}}]}]},
          {"date":"2026-10-17T16:00Z","competitions":[{"status":{"type":{"state":"pre"}},"competitors":[
            {"homeAway":"home","team":{"displayName":"Fenerbahce"}},{"homeAway":"away","team":{"displayName":"Alanyaspor"}}]}]}]}
        """;

    private const string Table = """
        {"children":[{"standings":{"entries":[
          {"team":{"displayName":"Besiktas"},"stats":[{"name":"rank","value":2},{"name":"points","value":15},{"name":"gamesPlayed","value":7},
            {"name":"wins","value":5},{"name":"ties","value":0},{"name":"losses","value":2},{"name":"pointDifferential","value":8,"displayValue":"+8"}]},
          {"team":{"displayName":"Galatasaray"},"stats":[{"name":"rank","value":1},{"name":"points","value":16},{"name":"gamesPlayed","value":7},
            {"name":"wins","value":5},{"name":"ties","value":1},{"name":"losses","value":1},{"name":"pointDifferential","value":5,"displayValue":"+5"}]}]}}]}
        """;

    private readonly List<string> _urls = [];

    private FootballService Service() => new(new HttpClient(new FakeHandler(r =>
    {
        var url = r.RequestUri!.ToString();
        lock (_urls)
        {
            _urls.Add(url);
        }

        return url switch
        {
            _ when url.EndsWith("/tur.1/teams", StringComparison.Ordinal) => FakeHandler.Json(Teams),
            _ when url.EndsWith("/teams/436/schedule", StringComparison.Ordinal) => FakeHandler.Json(Results),
            _ when url.EndsWith("/teams/436/schedule?fixture=true", StringComparison.Ordinal) => FakeHandler.Json(Fixtures),
            _ when url.Contains("/standings", StringComparison.Ordinal) => FakeHandler.Json(Table),
            _ when url.Contains("/teams", StringComparison.Ordinal) => FakeHandler.Json("""{"sports":[]}"""),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest),
        };
    })), () => new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData("fenerbahçe")]
    [InlineData("Fenerbahce")]
    [InlineData("fb")]
    [InlineData("fener")]
    [InlineData("FEN")]
    public async Task Team_is_found_by_name_or_nickname_and_last_match_comes_first(string team)
    {
        var matches = await Service().FindAsync(team, CancellationToken.None);

        Assert.Equal(
            ["Çaykur Rizespor 0-3 Fenerbahçe", "Fenerbahçe -Alanyaspor", "Galatasaray -Fenerbahçe", "Fenerbahçe 8-0 Eyüpspor", "Gaziantep FK 0-0 Fenerbahçe"],
            matches.Select(m => $"{m.Home} {m.HomeScore}-{m.AwayScore}{(m.HomeScore is null ? "" : " ")}{m.Away}".Replace("  ", " ")));
    }

    [Fact]
    public async Task Team_word_prefix_works()
    {
        var matches = await Service().FindAsync("başakşehir", CancellationToken.None);
        Assert.Contains(_urls, u => u.EndsWith("/teams/7914/schedule", StringComparison.Ordinal));
        Assert.Empty(matches);
    }

    [Fact]
    public async Task Team_list_is_cached()
    {
        var service = Service();
        await service.FindAsync("fb", CancellationToken.None);
        await service.FindAsync("galatasaray", CancellationToken.None);

        Assert.Single(_urls, u => u.EndsWith("/tur.1/teams", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_team_is_empty_without_errors()
    {
        Assert.Empty(await Service().FindAsync("zzzz", CancellationToken.None));
    }

    [Fact]
    public async Task Matches_plugin_shows_last_match_as_card_and_rest_as_rows()
    {
        var results = await InfoPlugins.MatchesAsync(Service(), "fb", _ => { }, CancellationToken.None);

        Assert.Equal("Çaykur Rizespor  0 – 3  Fenerbahçe", results[0].Card!.Headline);
        Assert.StartsWith("Bitti · ", results[0].Card!.Caption);
        Assert.Null(results[1].Card);
        Assert.Equal("Fenerbahçe  –  Alanyaspor", results[1].Title);
    }

    [Fact]
    public async Task League_table_is_sorted_by_rank()
    {
        var opened = new List<string>();
        var results = await InfoPlugins.TableAsync(Service(), "", opened.Add, CancellationToken.None);

        Assert.Equal(["1. Galatasaray", "2. Beşiktaş"], results.Select(r => r.Title));
        Assert.Equal("16 puan · 7 maç · 5G 1B 1M · averaj +5 · Süper Lig", results[0].Subtitle);
        results[0].Action(new LumoraGo.Plugin.ActionContext(LumoraGo.Plugin.ActionModifiers.None));
        Assert.Equal(["https://www.espn.com/soccer/standings/_/league/tur.1"], opened);
        Assert.Contains("https://site.api.espn.com/apis/v2/sports/soccer/tur.1/standings", _urls);
    }

    [Theory]
    [InlineData("", "tur.1")]
    [InlineData("premier", "eng.1")]
    [InlineData("la liga", "esp.1")]
    [InlineData("İspanya", "esp.1")]
    [InlineData("serie a", "ita.1")]
    [InlineData("bundes", "ger.1")]
    public void League_names(string text, string code) => Assert.Equal(code, FootballService.FindLeague(text)!.Value.Code);

    [Fact]
    public async Task Unknown_league_says_so()
    {
        var row = Assert.Single(await InfoPlugins.TableAsync(Service(), "mars", _ => { }, CancellationToken.None));
        Assert.Equal("\"mars\" ligi bilinmiyor", row.Title);
    }

    [Fact]
    public async Task Week_uses_monthly_scoreboard_not_date_ranges()
    {
        await Service().FindAsync("", CancellationToken.None);
        Assert.Equal(["https://site.api.espn.com/apis/site/v2/sports/soccer/tur.1/scoreboard?dates=202610"], _urls);
    }
}
