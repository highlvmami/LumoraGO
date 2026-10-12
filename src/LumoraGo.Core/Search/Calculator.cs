using System.Globalization;

namespace LumoraGo.Core.Search;

/// <summary>
/// Küçük bir ifade hesaplayıcı: + - * / % ^, parantez, ondalık için nokta veya virgül, sqrt/sin/cos/tan/log/ln/abs/round,
/// pi ve e. Dış kütüphane kullanmaz.
/// </summary>
public static class Calculator
{
    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqrt"] = Math.Sqrt,
        ["kok"] = Math.Sqrt,
        ["sin"] = x => Math.Sin(x * Math.PI / 180),
        ["cos"] = x => Math.Cos(x * Math.PI / 180),
        ["tan"] = x => Math.Tan(x * Math.PI / 180),
        ["log"] = Math.Log10,
        ["ln"] = Math.Log,
        ["abs"] = Math.Abs,
        ["round"] = Math.Round,
    };

    /// <summary>
    /// Metin bir hesap gibi görünüyorsa (en az bir rakam ve bir işlem ya da fonksiyon) ve hatasız hesaplanabiliyorsa sonucu döndürür.
    /// Tek başına bir sayı ("2026") hesap sayılmaz.
    /// </summary>
    public static bool TryEvaluate(string text, out double value)
    {
        value = 0;
        var input = text.Trim().TrimStart('=').Trim();
        if (input.Length == 0 || !input.Any(char.IsDigit) || !LooksLikeMath(input))
        {
            return false;
        }

        try
        {
            var parser = new Parser(input);
            value = parser.ParseExpression();
            parser.ExpectEnd();
            return double.IsFinite(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool LooksLikeMath(string input) =>
        input.IndexOfAny(['+', '*', '/', '^', '%', '×', '÷', '(']) >= 0
        || input.LastIndexOf('-') > 0
        || Functions.Keys.Any(f => input.Contains(f, StringComparison.OrdinalIgnoreCase));

    /// <summary>Türkçe biçimde gösterir: "1.234,5". En fazla 10 ondalık.</summary>
    public static string Format(double value, bool grouping = true)
    {
        var rounded = Math.Round(value, 10);
        return rounded.ToString(grouping ? "#,##0.##########" : "0.##########", CultureInfo.GetCultureInfo("tr-TR"));
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public void ExpectEnd()
        {
            SkipSpaces();
            if (_pos < text.Length)
            {
                throw new FormatException($"Beklenmeyen karakter: {text[_pos]}");
            }
        }

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                if (Accept('+')) value += ParseTerm();
                else if (Accept('-')) value -= ParseTerm();
                else return value;
            }
        }

        // term := power (('*' | '/' | '%') power)*
        private double ParseTerm()
        {
            var value = ParsePower();
            while (true)
            {
                if (Accept('*') || Accept('×')) value *= ParsePower();
                else if (Accept('/') || Accept('÷'))
                {
                    var divisor = ParsePower();
                    if (divisor == 0)
                    {
                        throw new FormatException("Sıfıra bölme");
                    }

                    value /= divisor;
                }
                else if (Accept('%')) value %= ParsePower();
                else return value;
            }
        }

        // power := unary ('^' power)?   (sağdan birleşir: 2^3^2 = 2^9)
        private double ParsePower()
        {
            var value = ParseUnary();
            return Accept('^') ? Math.Pow(value, ParsePower()) : value;
        }

        private double ParseUnary()
        {
            if (Accept('-')) return -ParseUnary();
            if (Accept('+')) return ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipSpaces();
            if (Accept('('))
            {
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }

            if (_pos < text.Length && char.IsLetter(text[_pos]))
            {
                var start = _pos;
                while (_pos < text.Length && char.IsLetter(text[_pos])) _pos++;
                var name = text[start.._pos];

                if (name.Equals("pi", StringComparison.OrdinalIgnoreCase)) return Math.PI;
                if (name.Equals("e", StringComparison.OrdinalIgnoreCase)) return Math.E;
                if (Functions.TryGetValue(name, out var function))
                {
                    Expect('(');
                    var argument = ParseExpression();
                    Expect(')');
                    return function(argument);
                }

                throw new FormatException($"Bilinmeyen ad: {name}");
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipSpaces();
            var start = _pos;
            var separators = 0;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] is '.' or ','))
            {
                if (text[_pos] is '.' or ',') separators++;
                _pos++;
            }

            // Tek ayraç ondalık sayılır ("2,5" veya "2.5"); birden fazlası belirsiz olduğu için hesap değil.
            if (_pos == start || separators > 1)
            {
                throw new FormatException("Sayı bekleniyordu");
            }

            var number = text[start.._pos].Replace(',', '.');
            return double.Parse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private bool Accept(char c)
        {
            SkipSpaces();
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private void Expect(char c)
        {
            if (!Accept(c))
            {
                throw new FormatException($"'{c}' bekleniyordu");
            }
        }

        private void SkipSpaces()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
