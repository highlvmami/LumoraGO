using LumoraGo.Core.Plugins;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class ConverterTests
{
    private const string RatesJson = """{"result":"success","rates":{"USD":1,"TRY":40,"EUR":0.8}}""";

    private static bool NoCurrency(string _) => false;

    [Theory]
    [InlineData("5 km mi", 3.1069, "mi")]
    [InlineData("5km to mi", 3.1069, "mi")]
    [InlineData("2,5 kg lb", 5.5116, "lb")]
    [InlineData("1 mil kaç metre", 1609.344, "m")]
    [InlineData("100 c f", 212, "f")]
    [InlineData("0 °C k", 273.15, "k")]
    [InlineData("1 gb mb", 1024, "mb")]
    [InlineData("90 dk saat", 1.5, "h")]
    [InlineData("100 km/h mph", 62.1371, "mph")]
    [InlineData("1 dönüm m2", 1000, "m2")]
    public void Converts_units(string text, double expected, string to)
    {
        Assert.True(UnitConverter.TryParse(text, NoCurrency, out var request));
        Assert.Equal(to, request.To);
        Assert.Equal(expected, UnitConverter.Convert(request), 3);
    }

    [Theory]
    [InlineData("5 km kg")]
    [InlineData("merhaba dünya")]
    [InlineData("5 km")]
    [InlineData("km mi")]
    [InlineData("5 km km")]
    public void Rejects_non_conversions(string text) =>
        Assert.False(UnitConverter.TryParse(text, NoCurrency, out _));

    [Theory]
    [InlineData("1.000,5", 1000.5)]
    [InlineData("1,000.5", 1000.5)]
    [InlineData("2,5", 2.5)]
    public void Reads_both_decimal_styles(string text, double expected)
    {
        Assert.True(UnitConverter.TryParseNumber(text, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task Currency_uses_cached_rates_and_aliases()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumorago-rates-{Guid.NewGuid():N}.json");
        try
        {
            var downloads = 0;
            var rates = new CurrencyRates(path, _ => { downloads++; return Task.FromResult(RatesJson); });
            Assert.False(rates.IsLoaded);
            Assert.True(rates.IsKnown("TRY"));

            await rates.RefreshIfStaleAsync(DateTime.UtcNow);
            await rates.RefreshIfStaleAsync(DateTime.UtcNow);
            Assert.Equal(1, downloads);

            Assert.True(UnitConverter.TryParse("100 dolar kaç tl", rates.IsKnown, out var request));
            Assert.Equal(UnitKind.Currency, request.Kind);
            Assert.True(rates.TryConvert(request.Amount, request.From, request.To, out var tl));
            Assert.Equal(4000, tl, 6);
            Assert.True(rates.TryConvert(100, "EUR", "TRY", out var eur));
            Assert.Equal(5000, eur, 6);

            var reloaded = new CurrencyRates(path, _ => throw new HttpRequestException());
            Assert.True(reloaded.TryConvert(1, "USD", "EUR", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Plugin_shows_result_and_copies_plain_number()
    {
        var host = new NullHost();
        var plugin = new ConverterPlugin(new CurrencyRates(null, async _ =>
        {
            await Task.Delay(100);
            return RatesJson;
        }));
        plugin.Init(new PluginContext(ConverterPlugin.Metadata, "", host));

        var unit = Assert.Single(await plugin.QueryAsync(new Query("1500 m km", null, "1500 m km"), CancellationToken.None));
        Assert.Equal("= 1,5 km", unit.Title);
        unit.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["1,5"], host.Copied);

        var first = Assert.Single(await plugin.QueryAsync(new Query("10 usd try", null, "10 usd try"), CancellationToken.None));
        Assert.StartsWith("Döviz kurları yükleniyor", first.Title);

        IReadOnlyList<Result> results = [];
        for (var i = 0; i < 50 && (results.Count == 0 || results[0].Title.StartsWith("Döviz")); i++)
        {
            await Task.Delay(20);
            results = await plugin.QueryAsync(new Query("10 usd try", null, "10 usd try"), CancellationToken.None);
        }

        Assert.Equal("= 400 TRY", results[0].Title);
        Assert.Empty(await plugin.QueryAsync(new Query("notepad", null, "notepad"), CancellationToken.None));
    }
}
