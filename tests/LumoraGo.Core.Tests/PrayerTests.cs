using LumoraGo.Core.Info;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class PrayerTests
{
    private static string Day(string imsak, string isha = "19:51") => """
        {"code":200,"status":"OK","data":{"timings":{"Fajr":"05:43","Sunrise":"07:07 (+03)","Dhuhr":"12:55","Asr":"16:01","Sunset":"18:32","Maghrib":"18:32","Isha":"ISHA","Imsak":"IMSAK"}}}
        """.Replace("ISHA", isha).Replace("IMSAK", imsak);

    private static (PrayerTimesService Service, FakeHandler Handler) Service() 
    {
        var handler = new FakeHandler(r => FakeHandler.Json(r.RequestUri!.AbsolutePath.Contains("15-10-2026") ? Day("05:34") : Day("05:33")));
        return (new PrayerTimesService(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Card_ShowsCountdownToNextPrayer()
    {
        var (service, handler) = Service();
        var results = await PrayerPlugins.PrayerAsync(service, "", "İzmir", _ => { }, new DateTime(2026, 10, 14, 17, 10, 0), CancellationToken.None);
        var card = results[0].Card!;
        Assert.Equal("Akşama 1 sa 22 dk", card.Headline);
        Assert.Contains("Akşam  18:32  ← sıradaki", card.Lines!);
        Assert.Contains("city=Izmir&country=Turkey&method=13", handler.Requests[0]);
        Assert.Contains("14-10-2026", handler.Requests[0]);
        Assert.Equal(7, results.Count);
    }

    [Fact]
    public async Task AfterIsha_CountsToTomorrowsImsak()
    {
        var (service, _) = Service();
        var results = await PrayerPlugins.PrayerAsync(service, "ankara", "", _ => { }, new DateTime(2026, 10, 14, 23, 0, 0), CancellationToken.None);
        Assert.Equal("İmsaka 6 sa 34 dk", results[0].Card!.Headline);
        Assert.Equal("Ankara namaz vakitleri", results[0].Title);
    }

    [Fact]
    public async Task Tomorrow_UsesNextDateAndCaches()
    {
        var (service, handler) = Service();
        var now = new DateTime(2026, 10, 14, 10, 0, 0);
        await PrayerPlugins.PrayerAsync(service, "yarın", "", _ => { }, now, CancellationToken.None);
        var results = await PrayerPlugins.PrayerAsync(service, "yarin", "", _ => { }, now, CancellationToken.None);
        Assert.Equal("Yarın imsak 05:34", results[0].Card!.Headline);
        Assert.Single(handler.Requests);
        Assert.Contains("city=Istanbul", handler.Requests[0]);
    }

    [Fact]
    public async Task UnknownCity_ShowsHint()
    {
        var service = new PrayerTimesService(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}", System.Net.HttpStatusCode.NotFound))));
        var result = Assert.Single(await PrayerPlugins.PrayerAsync(service, "xyz", "", _ => { }, DateTime.Now, CancellationToken.None));
        Assert.Contains("bulunamadı", result.Title);
    }

    [Fact]
    public void Ascii_StripsTurkishLetters() => Assert.Equal("Mugla Sanliurfa Izmir", PrayerTimesService.Ascii(" Muğla Şanlıurfa İzmir "));

    [Fact]
    public async Task Pharmacy_LinksUseCityAndIstanbulChamber()
    {
        var opened = new List<string>();
        var plugin = new PharmacyPlugin(() => "İzmir", opened.Add);
        var izmir = await plugin.QueryAsync(new Query("/eczane", "/eczane", ""), CancellationToken.None);
        Assert.Equal(3, izmir.Count);
        izmir[0].Action(new ActionContext(ActionModifiers.None));
        Assert.Contains("maps/search/n%C3%B6bet%C3%A7i%20eczane%20%C4%B0zmir", opened[0]);

        var kadikoy = await plugin.QueryAsync(new Query("/eczane kadıköy", "/eczane", "kadıköy"), CancellationToken.None);
        Assert.Contains(kadikoy, r => r.Title.Contains("İstanbul Eczacı Odası"));
    }
}
