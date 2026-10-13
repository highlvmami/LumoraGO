using System.Globalization;
using System.Text.Json;

namespace LumoraGo.Core.Info;

/// <param name="Points">Gün içi kapanışlar (eğri için).</param>
public sealed record Quote(string Symbol, string Name, double Price, double? PreviousClose, string Currency, string? Exchange, IReadOnlyList<double> Points, string Url, bool Delayed, string? Note = null)
{
    public double? Change => PreviousClose is { } p && p != 0 ? Price - p : null;

    public double? ChangePercent => PreviousClose is { } p && p != 0 ? (Price - p) / p * 100 : null;
}

/// <summary>
/// Hisse için Yahoo Finance grafik adresi (resmi değil, 15 dk gecikmeli), kripto için CoinGecko. BIST kodları için önce ".IS" denenir.
/// </summary>
public sealed class QuoteService(HttpClient http)
{
    internal static readonly Dictionary<string, (string Id, string Name)> Coins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["btc"] = ("bitcoin", "Bitcoin"),
        ["bitcoin"] = ("bitcoin", "Bitcoin"),
        ["eth"] = ("ethereum", "Ethereum"),
        ["ethereum"] = ("ethereum", "Ethereum"),
        ["sol"] = ("solana", "Solana"),
        ["xrp"] = ("ripple", "XRP"),
        ["bnb"] = ("binancecoin", "BNB"),
        ["doge"] = ("dogecoin", "Dogecoin"),
        ["ada"] = ("cardano", "Cardano"),
        ["avax"] = ("avalanche-2", "Avalanche"),
        ["trx"] = ("tron", "TRON"),
        ["ltc"] = ("litecoin", "Litecoin"),
        ["dot"] = ("polkadot", "Polkadot"),
        ["usdt"] = ("tether", "Tether"),
    };

    public static bool IsCoin(string symbol) => Coins.ContainsKey(symbol.Trim());

    public async Task<Quote?> GetAsync(string symbol, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (Coins.TryGetValue(symbol, out var coin))
        {
            return await CoinAsync(symbol, coin.Id, coin.Name, ct);
        }

        // "THYAO" BIST'te, "AAPL" ABD'de: önce .IS denenir, yoksa yazıldığı gibi.
        var candidates = symbol.Contains('.') || symbol.Contains('^') || symbol.Contains('=') || symbol.Contains('-')
            ? [symbol]
            : new[] { symbol + ".IS", symbol };
        foreach (var candidate in candidates)
        {
            if (await StockAsync(candidate, ct) is { } quote)
            {
                return quote;
            }
        }

        return null;
    }

    private async Task<Quote?> StockAsync(string symbol, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http,
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m", ct);
        return json is null ? null : ParseYahoo(json.RootElement);
    }

    internal static Quote? ParseYahoo(JsonElement root)
    {
        var result = root.Obj("chart").Arr("result").FirstOrDefault();
        if (result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var meta = result.Obj("meta");
        if (meta.Num("regularMarketPrice") is not { } price || meta.Str("symbol") is not { } symbol)
        {
            return null;
        }

        var closes = result.Obj("indicators").Arr("quote").FirstOrDefault().Arr("close")
            .Where(v => v.ValueKind == JsonValueKind.Number)
            .Select(v => v.GetDouble())
            .ToList();
        var name = meta.Str("shortName") ?? meta.Str("longName") ?? symbol;
        return new Quote(
            symbol,
            name,
            price,
            meta.Num("chartPreviousClose") ?? meta.Num("previousClose"),
            meta.Str("currency") ?? "",
            meta.Str("fullExchangeName") ?? meta.Str("exchangeName"),
            closes,
            "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(symbol),
            true);
    }

    private async Task<Quote?> CoinAsync(string symbol, string id, string name, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http,
            $"https://api.coingecko.com/api/v3/simple/price?ids={id}&vs_currencies=usd,try&include_24hr_change=true", ct);
        return json is null ? null : ParseCoin(symbol, id, name, json.RootElement);
    }

    internal static Quote? ParseCoin(string symbol, string id, string name, JsonElement root)
    {
        var coin = root.Obj(id);
        if (coin.Num("usd") is not { } usd)
        {
            return null;
        }

        // 24 saatlik değişimden önceki fiyatı geri hesapla ki kart diğerleriyle aynı biçimde gösterilsin.
        double? previous = coin.Num("usd_24h_change") is { } pct ? usd / (1 + (pct / 100)) : null;
        var tryPrice = coin.Num("try");
        return new Quote(symbol.ToUpperInvariant(), name, usd, previous, "USD", "CoinGecko",
            [], $"https://www.coingecko.com/tr/coins/{id}", false,
            tryPrice is { } t ? $"≈ {Money(t, "TRY")}" : null);
    }

    public static string Money(double value, string currency)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var digits = Math.Abs(value) >= 1000 ? "N2" : Math.Abs(value) >= 1 ? "N2" : "N6";
        var number = value.ToString(digits, tr);
        return currency.ToUpperInvariant() switch
        {
            "TRY" => $"{number} ₺",
            "USD" => $"${number}",
            "EUR" => $"€{number}",
            "GBP" => $"£{number}",
            "" => number,
            var c => $"{number} {c}",
        };
    }
}
