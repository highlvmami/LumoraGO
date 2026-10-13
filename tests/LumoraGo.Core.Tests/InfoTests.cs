using System.Net;
using System.Text.Json;
using LumoraGo.Core.Info;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class InfoTests
{
    private const string Tdk = """
        [{"madde":"kalem","anlamlarListe":[
            {"anlam":"Yazı yazmaya yarayan araç","ozelliklerListe":[{"tam_adi":"isim"}],"orneklerListe":[{"ornek":"Kalemini unutmuş."}]},
            {"anlam":"Resmî dairelerde yazı işlerinin yürütüldüğü bölüm","ozelliklerListe":[{"tam_adi":"isim"}]}],
          "atasozu":[{"madde":"kalem kılıçtan keskindir"}]},
         {"madde":"kalem","anlamlarListe":[{"anlam":"Ağaç fidanı aşılamakta kullanılan dal"}],"atasozu":[{"madde":"kalem kılıçtan keskindir"}]}]
        """;

    private const string English = """
        [{"word":"pen","phonetic":"/pɛn/","meanings":[
            {"partOfSpeech":"noun","definitions":[{"definition":"A tool for writing with ink.","example":"Sign with a pen.","synonyms":["quill"]}],"synonyms":["biro"]},
            {"partOfSpeech":"verb","definitions":[{"definition":"To write.","synonyms":[]}],"synonyms":[]}]}]
        """;

    private const string Forecast = """
        {"current":{"temperature_2m":18.4,"apparent_temperature":17.2,"weather_code":2,"wind_speed_10m":12.6},
         "hourly":{"time":["a","b","c"],"temperature_2m":[18.4,19.0,17.5]},
         "daily":{"time":["2026-10-13","2026-10-14","2026-10-15"],"weather_code":[2,61,0],
                  "temperature_2m_max":[24.2,21.0,23.0],"temperature_2m_min":[15.1,14.0,13.0],"precipitation_probability_max":[20,80,null]}}
        """;

    private const string Yahoo = """
        {"chart":{"result":[{"meta":{"currency":"TRY","symbol":"THYAO.IS","shortName":"TURK HAVA YOLLARI","fullExchangeName":"Istanbul",
          "regularMarketPrice":312.5,"chartPreviousClose":305.3},
          "indicators":{"quote":[{"close":[306.0,null,310.0,312.5]}]}}],"error":null}}
        """;

    private const string Espn = """
        {"events":[
          {"date":"2026-10-13T17:00Z","status":{"displayClock":"67'","type":{"state":"in"}},
           "competitions":[{"competitors":[
             {"homeAway":"home","score":"2","team":{"displayName":"Fenerbahce"}},
             {"homeAway":"away","score":"1","team":{"displayName":"Galatasaray"}}]}]},
          {"date":"2026-10-12T17:00Z","status":{"type":{"state":"post"}},
           "competitions":[{"competitors":[
             {"homeAway":"home","score":"0","team":{"displayName":"Besiktas"}},
             {"homeAway":"away","score":"0","team":{"displayName":"Trabzonspor"}}]}]}]}
        """;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Tdk_entries_are_merged_with_kinds_examples_and_proverbs()
    {
        var entry = DictionaryLookup.ParseTdk("kalem", Json(Tdk))!;

        Assert.Equal("kalem", entry.Word);
        Assert.Equal(3, entry.Senses.Count);
        Assert.Equal(new Sense("Yazı yazmaya yarayan araç", "isim", "Kalemini unutmuş."), entry.Senses[0]);
        Assert.Null(entry.Senses[2].Kind);
        Assert.Equal(["kalem kılıçtan keskindir"], entry.Phrases);
        Assert.Contains("1. (isim) Yazı yazmaya yarayan araç", entry.FullText());
    }

    [Fact]
    public void Tdk_not_found_object_means_no_entry() =>
        Assert.Null(DictionaryLookup.ParseTdk("xyz", Json("""{"error":"Sonuç bulunamadı"}""")));

    [Fact]
    public void English_entries_have_phonetic_and_synonyms()
    {
        var entry = DictionaryLookup.ParseEnglish("pen", Json(English))!;

        Assert.Equal("/pɛn/", entry.Phonetic);
        Assert.Equal(["A tool for writing with ink.", "To write."], entry.Senses.Select(s => s.Meaning));
        Assert.Equal("noun", entry.Senses[0].Kind);
        Assert.Equal(["quill", "biro"], entry.Synonyms);
    }

    [Fact]
    public void Forecast_is_parsed()
    {
        var report = WeatherService.Parse(new Place("İzmir", "İzmir", "Türkiye", 38.4, 27.1), Json(Forecast));

        Assert.Equal(18.4, report.Temperature);
        Assert.Equal(17.2, report.FeelsLike);
        Assert.Equal(2, report.Code);
        Assert.Equal([18.4, 19.0, 17.5], report.Hourly);
        Assert.Equal(3, report.Days.Count);
        Assert.Equal(new WeatherDay(new DateOnly(2026, 10, 14), 21.0, 14.0, 80, 61), report.Days[1]);
        Assert.Null(report.Days[2].RainChance);
        Assert.Equal("İzmir, Türkiye", report.Place.Label);
    }

    [Theory]
    [InlineData(0, "Açık")]
    [InlineData(63, "Yağmurlu")]
    [InlineData(95, "Gök gürültülü fırtına")]
    [InlineData(1234, "Bilinmiyor")]
    public void Weather_codes_are_turkish(int code, string text) => Assert.Equal(text, WeatherService.Describe(code).Text);

    [Fact]
    public void Yahoo_quote_has_change_and_points()
    {
        var quote = QuoteService.ParseYahoo(Json(Yahoo))!;

        Assert.Equal("THYAO.IS", quote.Symbol);
        Assert.Equal(312.5, quote.Price);
        Assert.Equal(7.2, quote.Change!.Value, 3);
        Assert.Equal([306.0, 310.0, 312.5], quote.Points);
        Assert.Equal("312,50 ₺", QuoteService.Money(quote.Price, quote.Currency));
    }

    [Fact]
    public void Coin_quote_uses_the_24h_change()
    {
        var quote = QuoteService.ParseCoin("btc", "bitcoin", "Bitcoin",
            Json("""{"bitcoin":{"usd":110000,"usd_24h_change":10,"try":4500000,"try_24h_change":10}}"""))!;

        Assert.Equal("BTC", quote.Symbol);
        Assert.Equal(10, quote.ChangePercent!.Value, 3);
        Assert.Equal("≈ 4.500.000,00 ₺", quote.Note);
        Assert.Equal("$110.000,00", QuoteService.Money(quote.Price, quote.Currency));
    }

    [Fact]
    public void Espn_scoreboard_is_parsed()
    {
        var matches = FootballService.Parse("Süper Lig", Json(Espn));

        Assert.Equal(2, matches.Count);
        Assert.Equal(new Match("Süper Lig", "Fenerbahce", "Galatasaray", 2, 1, MatchState.Live, "Canlı 67'", new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc)), matches[0]);
        Assert.Equal(MatchState.Finished, matches[1].State);
        Assert.Equal("Bitti", matches[1].Status);
    }

    // ---- Ağ üzerinden uçtan uca (sahte sunucu) ----

    private static HttpClient Http(Func<Uri, HttpResponseMessage> respond) => new(new FakeHandler(r => respond(r.RequestUri!)));

    private static HttpResponseMessage Ok(string json) => FakeHandler.Json(json);

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    [Fact]
    public async Task Define_falls_back_to_english_and_copies_meanings()
    {
        var http = Http(u => u.Host == "sozluk.gov.tr" ? Ok("""{"error":"Sonuç bulunamadı"}""") : Ok(English));
        var host = new NullHost();
        var opened = new List<string>();

        var results = await InfoPlugins.DefineAsync(new DictionaryLookup(http), "pen", host, opened.Add, CancellationToken.None);

        Assert.Equal("A tool for writing with ink.", results[0].Title);
        Assert.Equal("1. noun “Sign with a pen.” · Enter ile kopyala", results[0].Subtitle);
        Assert.Contains("A tool for writing", results[0].PreviewText);
        results[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["A tool for writing with ink."], host.Copied);
        Assert.Contains(results, r => r.Title == "Eş anlamlılar: quill, biro");
        results[^1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://www.merriam-webster.com/dictionary/pen"], opened);
    }

    [Fact]
    public async Task Define_turkish_word_uses_tdk()
    {
        var http = Http(u => Ok(Tdk));
        var results = await InfoPlugins.DefineAsync(new DictionaryLookup(http), "kalem", null, _ => { }, CancellationToken.None);

        Assert.Equal("Yazı yazmaya yarayan araç", results[0].Title);
        Assert.Contains(results, r => r.Title == "kalem kılıçtan keskindir");
    }

    [Fact]
    public async Task Weather_card_for_a_city_and_tomorrow()
    {
        var http = Http(u => u.Host.StartsWith("geocoding")
            ? Ok("""{"results":[{"name":"İzmir","admin1":"İzmir","country":"Türkiye","latitude":38.4,"longitude":27.1}]}""")
            : Ok(Forecast));
        var weather = new WeatherService(http);

        var now = await InfoPlugins.WeatherAsync(weather, "", "izmir", _ => { }, CancellationToken.None);
        Assert.Equal("İzmir, Türkiye · şu an", now[0].Title);
        Assert.Equal("18°", now[0].Card!.Headline);
        Assert.Equal("Parçalı bulutlu · Hissedilen 17°", now[0].Card!.Caption);
        Assert.Equal("Bugün en yüksek 24°, en düşük 15° · Yağış olasılığı %20", now[0].Card!.Lines![0]);
        Assert.Equal("emoji:⛅", now[0].IconPath);
        Assert.Equal(3, now.Count);

        var tomorrow = await InfoPlugins.WeatherAsync(weather, "", "izmir yarın", _ => { }, CancellationToken.None);
        Assert.Equal("İzmir, Türkiye · yarın", tomorrow[0].Title);
        Assert.Equal("21° / 14°", tomorrow[0].Card!.Headline);
        Assert.Equal("Hafif yağmurlu", tomorrow[0].Card!.Caption);
    }

    [Fact]
    public async Task Weather_without_city_uses_setting_then_ip()
    {
        var asked = new List<string>();
        var http = Http(u =>
        {
            asked.Add(u.Host);
            return u.Host switch
            {
                "ipwho.is" => Ok("""{"success":true,"city":"Ankara","region":"Ankara","country":"Türkiye","latitude":39.9,"longitude":32.8}"""),
                _ => Ok(Forecast),
            };
        });

        var results = await InfoPlugins.WeatherAsync(new WeatherService(http), "", "", _ => { }, CancellationToken.None);

        Assert.Equal("Ankara, Türkiye · şu an", results[0].Title);
        Assert.Equal(["ipwho.is", "api.open-meteo.com"], asked);
    }

    [Fact]
    public async Task Bist_codes_try_the_istanbul_suffix_first_then_plain()
    {
        var asked = new List<string>();
        var http = Http(u =>
        {
            asked.Add(u.AbsolutePath);
            return u.AbsolutePath.EndsWith("/AAPL") ? Ok(Yahoo.Replace("THYAO.IS", "AAPL").Replace("TRY", "USD")) : NotFound();
        });
        var host = new NullHost();

        var results = await InfoPlugins.QuotesAsync(new QuoteService(http), "aapl", host, _ => { }, CancellationToken.None);

        Assert.Equal(["/v8/finance/chart/AAPL.IS", "/v8/finance/chart/AAPL"], asked);
        var card = results[0].Card!;
        Assert.Equal("$312,50", card.Headline);
        Assert.Equal("+%2,36 (+7,20) · Istanbul", card.Caption);
        Assert.True(card.Positive);
        Assert.Equal(3, card.Trend!.Count);
        results[0].ContextMenu![1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["$312,50"], host.Copied);
    }

    [Fact]
    public async Task Unknown_symbol_says_so()
    {
        var results = await InfoPlugins.QuotesAsync(new QuoteService(Http(_ => NotFound())), "zzzz", null, _ => { }, CancellationToken.None);
        Assert.StartsWith("\"zzzz\" bulunamadı", Assert.Single(results).Title);
    }

    [Fact]
    public async Task Team_search_finds_matches_across_leagues_with_turkish_letters()
    {
        var http = Http(u => u.AbsolutePath.Contains("/tur.1/") ? Ok(Espn) : Ok("""{"events":[]}"""));
        var results = await InfoPlugins.MatchesAsync(new FootballService(http), "fenerbahçe", _ => { }, CancellationToken.None);

        var card = Assert.Single(results).Card!;
        Assert.Equal("Fenerbahce  2 – 1  Galatasaray", card.Headline);
        Assert.Equal("Canlı 67'", card.Caption);
    }

    [Fact]
    public async Task Team_search_looks_weeks_back_and_ahead_and_orders_last_then_next()
    {
        const string Season = """
            {"events":[
              {"date":"2026-09-20T17:00Z","status":{"type":{"state":"post"}},
               "competitions":[{"competitors":[
                 {"homeAway":"home","score":"3","team":{"displayName":"Fenerbahce"}},
                 {"homeAway":"away","score":"0","team":{"displayName":"Rizespor"}}]}]},
              {"date":"2026-10-05T17:00Z","status":{"type":{"state":"post"}},
               "competitions":[{"competitors":[
                 {"homeAway":"home","score":"1","team":{"displayName":"Samsunspor"}},
                 {"homeAway":"away","score":"1","team":{"displayName":"Fenerbahçe"}}]}]},
              {"date":"2026-10-25T17:00Z","status":{"type":{"state":"pre"}},
               "competitions":[{"competitors":[
                 {"homeAway":"home","team":{"displayName":"Fenerbahce"}},
                 {"homeAway":"away","team":{"displayName":"Galatasaray"}}]}]},
              {"date":"2026-10-18T17:00Z","status":{"type":{"state":"pre"}},
               "competitions":[{"competitors":[
                 {"homeAway":"home","team":{"displayName":"Besiktas"}},
                 {"homeAway":"away","team":{"displayName":"Trabzonspor"}}]}]}]}
            """;
        var urls = new List<string>();
        var http = Http(u =>
        {
            lock (urls)
            {
                urls.Add(u.ToString());
            }

            return u.AbsolutePath.Contains("/tur.1/") ? Ok(Season) : Ok("""{"events":[]}""");
        });
        var football = new FootballService(http, () => new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));

        var matches = await football.FindAsync("fb", CancellationToken.None);

        Assert.All(urls, u => Assert.Contains("?dates=20260922-20261110&limit=500", u));
        Assert.Equal(FootballService.Leagues.Length, urls.Count);
        Assert.Equal(
            ["Samsunspor-Fenerbahçe", "Fenerbahce-Galatasaray", "Fenerbahce-Rizespor"],
            matches.Select(m => $"{m.Home}-{m.Away}"));
    }

    [Fact]
    public async Task Empty_team_shows_the_current_super_lig_week_only()
    {
        var urls = new List<string>();
        var http = Http(u =>
        {
            lock (urls)
            {
                urls.Add(u.ToString());
            }

            return Ok(Espn);
        });

        var matches = await new FootballService(http).FindAsync(" ", CancellationToken.None);

        Assert.Equal(["https://site.api.espn.com/apis/site/v2/sports/soccer/tur.1/scoreboard"], urls);
        Assert.Equal(MatchState.Live, matches[0].State);
    }

    [Theory]
    [InlineData("TK1980", "TK1980")]
    [InlineData("tk 1980", "TK1980")]
    [InlineData("pc2018", "PC2018")]
    [InlineData("u2 8812", "U28812")]
    public async Task Flight_codes_get_a_tracking_row(string text, string code)
    {
        var opened = new List<string>();
        var plugin = new InfoPlugins.FlightPlugin(opened.Add);

        var row = Assert.Single(await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None));
        Assert.Equal($"{code} uçuşunu takip et", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal($"https://www.flightradar24.com/data/flights/{code.ToLowerInvariant()}", opened[0]);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("tk")]
    [InlineData("12345")]
    [InlineData("abc123")]
    public async Task Other_text_is_not_a_flight(string text) =>
        Assert.Empty(await new InfoPlugins.FlightPlugin(_ => { }).QueryAsync(new Query(text, null, text), CancellationToken.None));

    // ---- Canlı sorgu eklentisi ----

    [Fact]
    public async Task Live_lookup_waits_for_typing_caches_and_refreshes()
    {
        var calls = new List<string>();
        var host = new NullHost();
        var plugin = new LiveLookupPlugin(new PluginMetadata("t.live", "Deneme", "/dn"), "Örnek: /dn x", (arg, _, _) =>
        {
            lock (calls)
            {
                calls.Add(arg);
            }

            return Task.FromResult<IReadOnlyList<Result>>([new Result($"sonuç {arg}", null, null, 10, _ => true)]);
        }, debounce: TimeSpan.FromMilliseconds(50));
        plugin.Init(new PluginContext(plugin.Metadata, "", host));

        Assert.Equal("Örnek: /dn x", Assert.Single(await plugin.QueryAsync(new Query("/dn", "/dn", ""), CancellationToken.None)).Subtitle);
        Assert.Equal("Deneme: yükleniyor…", (await plugin.QueryAsync(new Query("/dn a", "/dn", "a"), CancellationToken.None))[0].Title);
        await plugin.QueryAsync(new Query("/dn ab", "/dn", "ab"), CancellationToken.None);
        await Wait.Until(() => host.Refreshes.Contains("/dn"));

        Assert.Equal("sonuç ab", Assert.Single(await plugin.QueryAsync(new Query("/dn ab", "/dn", "ab"), CancellationToken.None)).Title);
        Assert.Equal(["ab"], calls); // "a" yazarken iptal edildi, sorulmadı
        await plugin.QueryAsync(new Query("/dn AB", "/dn", "AB"), CancellationToken.None);
        Assert.Single(calls); // önbellekten
    }

    [Fact]
    public async Task Live_lookup_shows_errors_and_empty_results()
    {
        var host = new NullHost();
        var plugin = new LiveLookupPlugin(new PluginMetadata("t.live2", "Deneme", "/dm"), "ipucu", (arg, _, _) =>
            arg == "hata" ? throw new HttpRequestException("bağlantı yok") : Task.FromResult<IReadOnlyList<Result>>([]),
            debounce: TimeSpan.Zero);
        plugin.Init(new PluginContext(plugin.Metadata, "", host));

        await plugin.QueryAsync(new Query("/dm hata", "/dm", "hata"), CancellationToken.None);
        await Wait.Until(() => host.Refreshes.Count == 1);
        Assert.Equal("Bilgi alınamadı — bağlantı yok", (await plugin.QueryAsync(new Query("/dm hata", "/dm", "hata"), CancellationToken.None))[0].Title);

        await plugin.QueryAsync(new Query("/dm boş", "/dm", "boş"), CancellationToken.None);
        await Wait.Until(() => host.Refreshes.Count == 2);
        Assert.Equal("Sonuç bulunamadı", (await plugin.QueryAsync(new Query("/dm boş", "/dm", "boş"), CancellationToken.None))[0].Title);
    }
}
