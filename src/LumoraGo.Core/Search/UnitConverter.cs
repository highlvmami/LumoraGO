using System.Globalization;
using System.Text.RegularExpressions;

namespace LumoraGo.Core.Search;

public enum UnitKind
{
    Length,
    Mass,
    Volume,
    Temperature,
    Data,
    Time,
    Speed,
    Area,
    Currency,
}

/// <param name="Amount">Yazılan miktar.</param>
/// <param name="From">Kaynak birim kodu ("km", "USD").</param>
/// <param name="To">Hedef birim kodu.</param>
public sealed record ConversionRequest(double Amount, string From, string To, UnitKind Kind);

/// <summary>"5 km mi", "100 usd try", "30 c f", "2,5 kg to lb" gibi çevirme isteklerini okur ve hesaplar.</summary>
public static partial class UnitConverter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private sealed record Unit(string Code, UnitKind Kind, double Factor, string Display);

    // Factor: temel birime çarpan (uzunluk: metre, kütle: kilogram, hacim: litre, veri: bayt, zaman: saniye, hız: m/s, alan: m²).
    private static readonly Unit[] Units =
    [
        new("mm", UnitKind.Length, 0.001, "mm"),
        new("cm", UnitKind.Length, 0.01, "cm"),
        new("m", UnitKind.Length, 1, "m"),
        new("km", UnitKind.Length, 1000, "km"),
        new("in", UnitKind.Length, 0.0254, "inç"),
        new("ft", UnitKind.Length, 0.3048, "ft"),
        new("yd", UnitKind.Length, 0.9144, "yarda"),
        new("mi", UnitKind.Length, 1609.344, "mil"),
        new("nmi", UnitKind.Length, 1852, "deniz mili"),

        new("mg", UnitKind.Mass, 0.000001, "mg"),
        new("g", UnitKind.Mass, 0.001, "g"),
        new("kg", UnitKind.Mass, 1, "kg"),
        new("t", UnitKind.Mass, 1000, "ton"),
        new("oz", UnitKind.Mass, 0.028349523125, "ons"),
        new("lb", UnitKind.Mass, 0.45359237, "lb"),

        new("ml", UnitKind.Volume, 0.001, "ml"),
        new("l", UnitKind.Volume, 1, "L"),
        new("m3", UnitKind.Volume, 1000, "m³"),
        new("gal", UnitKind.Volume, 3.785411784, "galon"),
        new("floz", UnitKind.Volume, 0.0295735295625, "fl oz"),
        new("cup", UnitKind.Volume, 0.2365882365, "su bardağı"),

        new("c", UnitKind.Temperature, 0, "°C"),
        new("f", UnitKind.Temperature, 0, "°F"),
        new("k", UnitKind.Temperature, 0, "K"),

        new("b", UnitKind.Data, 1, "B"),
        new("kb", UnitKind.Data, 1024, "KB"),
        new("mb", UnitKind.Data, 1024d * 1024, "MB"),
        new("gb", UnitKind.Data, 1024d * 1024 * 1024, "GB"),
        new("tb", UnitKind.Data, 1024d * 1024 * 1024 * 1024, "TB"),

        new("ms", UnitKind.Time, 0.001, "ms"),
        new("s", UnitKind.Time, 1, "sn"),
        new("min", UnitKind.Time, 60, "dk"),
        new("h", UnitKind.Time, 3600, "saat"),
        new("d", UnitKind.Time, 86400, "gün"),
        new("wk", UnitKind.Time, 604800, "hafta"),
        new("yr", UnitKind.Time, 31557600, "yıl"),

        new("kmh", UnitKind.Speed, 1 / 3.6, "km/sa"),
        new("mps", UnitKind.Speed, 1, "m/s"),
        new("mph", UnitKind.Speed, 0.44704, "mph"),
        new("kn", UnitKind.Speed, 1852 / 3600d, "knot"),

        new("m2", UnitKind.Area, 1, "m²"),
        new("km2", UnitKind.Area, 1_000_000, "km²"),
        new("ha", UnitKind.Area, 10_000, "hektar"),
        new("donum", UnitKind.Area, 1000, "dönüm"),
        new("acre", UnitKind.Area, 4046.8564224, "acre"),
        new("ft2", UnitKind.Area, 0.09290304, "ft²"),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["milimetre"] = "mm", ["santim"] = "cm", ["santimetre"] = "cm", ["metre"] = "m", ["kilometre"] = "km",
        ["inc"] = "in", ["inç"] = "in", ["inch"] = "in", ["\""] = "in", ["feet"] = "ft", ["foot"] = "ft", ["fit"] = "ft",
        ["yarda"] = "yd", ["yard"] = "yd", ["mil"] = "mi", ["mile"] = "mi", ["miles"] = "mi",
        ["gram"] = "g", ["gr"] = "g", ["kilo"] = "kg", ["kilogram"] = "kg", ["ton"] = "t", ["ons"] = "oz", ["ounce"] = "oz",
        ["pound"] = "lb", ["lbs"] = "lb", ["libre"] = "lb",
        ["litre"] = "l", ["lt"] = "l", ["liter"] = "l", ["mililitre"] = "ml", ["galon"] = "gal", ["gallon"] = "gal",
        ["bardak"] = "cup", ["metreküp"] = "m3", ["m³"] = "m3",
        ["°c"] = "c", ["celsius"] = "c", ["santigrat"] = "c", ["derece"] = "c", ["°f"] = "f", ["fahrenheit"] = "f", ["kelvin"] = "k",
        ["bayt"] = "b", ["byte"] = "b", ["kilobayt"] = "kb", ["megabayt"] = "mb", ["gigabayt"] = "gb", ["terabayt"] = "tb",
        ["sn"] = "s", ["saniye"] = "s", ["sec"] = "s", ["dk"] = "min", ["dakika"] = "min", ["sa"] = "h", ["saat"] = "h",
        ["hour"] = "h", ["hours"] = "h", ["gün"] = "d", ["gun"] = "d", ["day"] = "d", ["days"] = "d",
        ["hafta"] = "wk", ["week"] = "wk", ["yıl"] = "yr", ["yil"] = "yr", ["year"] = "yr",
        ["km/h"] = "kmh", ["km/sa"] = "kmh", ["kmsa"] = "kmh", ["m/s"] = "mps", ["knot"] = "kn", ["knots"] = "kn",
        ["m²"] = "m2", ["km²"] = "km2", ["hektar"] = "ha", ["dönüm"] = "donum", ["dekar"] = "donum", ["ft²"] = "ft2",

        ["dolar"] = "USD", ["$"] = "USD", ["euro"] = "EUR", ["avro"] = "EUR", ["€"] = "EUR", ["tl"] = "TRY", ["lira"] = "TRY",
        ["₺"] = "TRY", ["sterlin"] = "GBP", ["£"] = "GBP", ["yen"] = "JPY", ["frank"] = "CHF", ["ruble"] = "RUB",
    };

    // "5 km mi", "5km to mi", "2,5 kg -> lb", "100 usd = try", "30 c in f"
    [GeneratedRegex(@"^\s*(?<num>-?\d[\d.,]*)\s*(?<from>[^\s\d][^\s]*)\s+(?:(?:to|in|->|=|>|kaç|kac)\s+)?(?<to>[^\s\d][^\s]*)\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RequestPattern();

    /// <param name="isCurrency">Üç harfli bir kodun bilinen bir para birimi olup olmadığını söyler.</param>
    public static bool TryParse(string text, Func<string, bool> isCurrency, out ConversionRequest request)
    {
        request = null!;
        var match = RequestPattern().Match(text);
        if (!match.Success || !TryParseNumber(match.Groups["num"].Value, out var amount))
        {
            return false;
        }

        var from = Normalize(match.Groups["from"].Value);
        var to = Normalize(match.Groups["to"].Value);

        if (Find(from) is { } fromUnit && Find(to) is { } toUnit && fromUnit.Kind == toUnit.Kind && fromUnit.Code != toUnit.Code)
        {
            request = new ConversionRequest(amount, fromUnit.Code, toUnit.Code, fromUnit.Kind);
            return true;
        }

        var fromCurrency = from.ToUpperInvariant();
        var toCurrency = to.ToUpperInvariant();
        if (fromCurrency != toCurrency && isCurrency(fromCurrency) && isCurrency(toCurrency))
        {
            request = new ConversionRequest(amount, fromCurrency, toCurrency, UnitKind.Currency);
            return true;
        }

        return false;
    }

    /// <summary>Birim çevirir (para birimi hariç).</summary>
    public static double Convert(ConversionRequest request)
    {
        if (request.Kind == UnitKind.Temperature)
        {
            var celsius = request.From switch
            {
                "f" => (request.Amount - 32) * 5 / 9,
                "k" => request.Amount - 273.15,
                _ => request.Amount,
            };
            return request.To switch
            {
                "f" => (celsius * 9 / 5) + 32,
                "k" => celsius + 273.15,
                _ => celsius,
            };
        }

        return request.Amount * Find(request.From)!.Factor / Find(request.To)!.Factor;
    }

    public static string Display(string code) => Find(code)?.Display ?? code;

    public static string FormatNumber(double value, bool grouping = true)
    {
        var magnitude = Math.Abs(value);
        var decimals = magnitude >= 100 ? 2 : magnitude >= 1 ? 4 : 6;
        var pattern = (grouping ? "#,##0." : "0.") + new string('#', decimals);
        return Math.Round(value, decimals).ToString(pattern, Turkish);
    }

    private static Unit? Find(string code) => Array.Find(Units, u => u.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string unit)
    {
        var trimmed = unit.Trim().TrimEnd('?', '.', ',');
        return Aliases.TryGetValue(trimmed, out var code) ? code : trimmed.ToLowerInvariant();
    }

    /// <summary>"1,5", "1.5", "1.000,5" ve "1,000.5" biçimlerini okur.</summary>
    internal static bool TryParseNumber(string text, out double value)
    {
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        string normalized;
        if (lastComma >= 0 && lastDot >= 0)
        {
            normalized = lastComma > lastDot
                ? text.Replace(".", "").Replace(',', '.')
                : text.Replace(",", "");
        }
        else
        {
            normalized = text.Replace(',', '.');
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
