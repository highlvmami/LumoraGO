using System.Globalization;
using System.Text.RegularExpressions;

namespace LumoraGo.Core.Search;

/// <summary>Bir renk: 0-255 kanallar ve 0-1 arası saydamlık.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, double A = 1);

/// <summary>"#1e90ff", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)" gibi renk kodlarını okur ve birbirine çevirir.</summary>
public static partial class ColorCode
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [GeneratedRegex(@"^\s*#?(?<hex>[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\s*$")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^\s*(?<fn>rgba?|hsla?)\s*\(\s*(?<a>[-\d.]+)(?:deg)?\s*[,\s]\s*(?<b>[-\d.]+%?)\s*[,\s]\s*(?<c>[-\d.]+%?)\s*(?:[,/]\s*(?<d>[\d.]+%?)\s*)?\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionPattern();

    /// <summary>Metin bir renk koduysa rengi verir. Başında # olmayan onaltılık yalnızca 6 veya 8 hane ve en az bir harf içerince kabul edilir.</summary>
    public static bool TryParse(string text, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = HexPattern().Match(text);
        if (hex.Success)
        {
            var digits = hex.Groups["hex"].Value;
            // "123456" ya da "face" gibi sayı/kelime aramalarını renk sanmamak için # yoksa 6+ hane ve en az bir harf gerekir.
            if (!text.TrimStart().StartsWith('#') && (digits.Length < 6 || digits.All(char.IsAsciiDigit)))
            {
                return false;
            }

            if (digits.Length <= 4)
            {
                digits = string.Concat(digits.Select(c => $"{c}{c}"));
            }

            var r = byte.Parse(digits[..2], NumberStyles.HexNumber, Inv);
            var g = byte.Parse(digits[2..4], NumberStyles.HexNumber, Inv);
            var b = byte.Parse(digits[4..6], NumberStyles.HexNumber, Inv);
            var a = digits.Length == 8 ? byte.Parse(digits[6..8], NumberStyles.HexNumber, Inv) / 255.0 : 1;
            color = new Rgba(r, g, b, Math.Round(a, 2));
            return true;
        }

        var fn = FunctionPattern().Match(text);
        if (!fn.Success)
        {
            return false;
        }

        var alpha = 1.0;
        if (fn.Groups["d"].Success && !TryChannel(fn.Groups["d"].Value, 1, out alpha))
        {
            return false;
        }

        if (fn.Groups["fn"].Value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryChannel(fn.Groups["a"].Value, 255, out var r) || !TryChannel(fn.Groups["b"].Value, 255, out var g) || !TryChannel(fn.Groups["c"].Value, 255, out var b))
            {
                return false;
            }

            color = new Rgba(ToByte(r), ToByte(g), ToByte(b), alpha);
            return true;
        }

        if (!double.TryParse(fn.Groups["a"].Value, NumberStyles.Float, Inv, out var h)
            || !TryPercent(fn.Groups["b"].Value, out var s)
            || !TryPercent(fn.Groups["c"].Value, out var l))
        {
            return false;
        }

        color = FromHsl(h, s, l) with { A = alpha };
        return true;
    }

    public static string ToHex(Rgba c) =>
        c.A < 1 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{(int)Math.Round(c.A * 255):X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string ToRgb(Rgba c) =>
        c.A < 1 ? $"rgba({c.R}, {c.G}, {c.B}, {c.A.ToString("0.##", Inv)})" : $"rgb({c.R}, {c.G}, {c.B})";

    public static string ToHsl(Rgba c)
    {
        var (h, s, l) = ToHslParts(c);
        return c.A < 1
            ? $"hsla({h}, {s}%, {l}%, {c.A.ToString("0.##", Inv)})"
            : $"hsl({h}, {s}%, {l}%)";
    }

    internal static (int H, int S, int L) ToHslParts(Rgba c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0, s = 0;
        var d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r)
            {
                h = ((g - b) / d) + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = ((b - r) / d) + 2;
            }
            else
            {
                h = ((r - g) / d) + 4;
            }

            h *= 60;
        }

        return ((int)Math.Round(h) % 360, (int)Math.Round(s * 100), (int)Math.Round(l * 100));
    }

    internal static Rgba FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        if (s == 0)
        {
            var v = ToByte(l * 255);
            return new Rgba(v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return new Rgba(ToByte(Hue(p, q, h + (1.0 / 3)) * 255), ToByte(Hue(p, q, h) * 255), ToByte(Hue(p, q, h - (1.0 / 3)) * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        return t switch
        {
            < 1.0 / 6 => p + ((q - p) * 6 * t),
            < 1.0 / 2 => q,
            < 2.0 / 3 => p + ((q - p) * ((2.0 / 3) - t) * 6),
            _ => p,
        };
    }

    /// <summary>"128" veya "50%" değerini [0, max] aralığında okur.</summary>
    private static bool TryChannel(string text, double max, out double value)
    {
        if (text.EndsWith('%'))
        {
            if (!TryPercent(text, out var fraction))
            {
                value = 0;
                return false;
            }

            value = fraction * max;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, Inv, out value) && value >= 0 && value <= max;
    }

    private static bool TryPercent(string text, out double fraction)
    {
        fraction = 0;
        return double.TryParse(text.TrimEnd('%'), NumberStyles.Float, Inv, out var p) && p >= 0 && p <= 100 && (fraction = p / 100) >= 0;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
