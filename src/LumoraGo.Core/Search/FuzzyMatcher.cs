using System.Globalization;
using System.Text;

namespace LumoraGo.Core.Search;

/// <summary>
/// Bulanık arama: sorgunun harfleri hedefte aynı sırayla geçmeli. Kelime başı, ardışık harf ve baştan eşleşme
/// ödüllendirilir, aradaki atlanan harfler cezalandırılır. "vsc" → "Visual Studio Code", "chr" → "Google Chrome".
/// Büyük/küçük harf ve Türkçe karakterler (ç→c, ş→s, ı→i...) fark etmez.
/// </summary>
public static class FuzzyMatcher
{
    private const int MatchScore = 16;
    private const int WordStartBonus = 24;
    private const int FirstCharBonus = 8;
    private const int ConsecutiveBonus = 12;
    private const int GapPenalty = 1;
    private const int PrefixBonus = 40;
    private const int MaxTargetLength = 256;

    /// <returns>Eşleşme puanı; eşleşmiyorsa 0.</returns>
    public static int Score(string query, string target) =>
        Score(Fold(query), new FoldedText(target));

    /// <param name="foldedQuery"><see cref="Fold"/> ile hazırlanmış sorgu.</param>
    /// <param name="target">Önceden katlanmış hedef; her tuşta yeniden katlamamak için önbelleklenir.</param>
    public static int Score(string foldedQuery, FoldedText target)
    {
        var q = foldedQuery;
        var t = target.Folded;
        var m = q.Length;
        var n = Math.Min(t.Length, MaxTargetLength);
        if (m == 0 || m > n)
        {
            return 0;
        }

        // Hızlı ön eleme: harfler sırayla geçmiyorsa DP'ye hiç girme.
        var qi = 0;
        for (var i = 0; i < n && qi < m; i++)
        {
            if (t[i] == q[qi])
            {
                qi++;
            }
        }

        if (qi < m)
        {
            return 0;
        }

        // prev[j]: sorgunun (i-1). harfi hedefin j. konumunda eşleşirken en iyi puan. int.MinValue = mümkün değil.
        var prev = new int[n];
        var curr = new int[n];
        for (var j = 0; j < n; j++)
        {
            prev[j] = t[j] == q[0] ? CharScore(target, j) - (j * GapPenalty / 4) : int.MinValue;
        }

        for (var i = 1; i < m; i++)
        {
            // bestBefore: k < j-1 için max(prev[k] - gap ceza). Doğrusal güncellenir.
            var bestBefore = int.MinValue;
            for (var j = 0; j < n; j++)
            {
                if (bestBefore != int.MinValue)
                {
                    bestBefore -= GapPenalty;
                }

                if (j >= 2 && prev[j - 2] != int.MinValue)
                {
                    bestBefore = Math.Max(bestBefore, prev[j - 2] - GapPenalty);
                }

                if (t[j] != q[i])
                {
                    curr[j] = int.MinValue;
                    continue;
                }

                var viaConsecutive = j >= 1 && prev[j - 1] != int.MinValue ? prev[j - 1] + ConsecutiveBonus : int.MinValue;
                var best = Math.Max(viaConsecutive, bestBefore);
                curr[j] = best == int.MinValue ? int.MinValue : best + CharScore(target, j);
            }

            (prev, curr) = (curr, prev);
        }

        var result = int.MinValue;
        for (var j = 0; j < n; j++)
        {
            result = Math.Max(result, prev[j]);
        }

        if (result == int.MinValue)
        {
            return 0;
        }

        if (t.StartsWith(q, StringComparison.Ordinal))
        {
            result += PrefixBonus;
        }

        return Math.Max(result, 1);
    }

    private static int CharScore(FoldedText target, int index)
    {
        var score = MatchScore;
        if (target.IsWordStart(index))
        {
            score += WordStartBonus;
        }

        if (index == 0)
        {
            score += FirstCharBonus;
        }

        return score;
    }

    /// <summary>Küçük harfe çevirir, aksanları ve Türkçe harfleri ASCII karşılığına indirir. Uzunluk korunur.</summary>
    public static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(FoldChar(c));
        }

        return builder.ToString();
    }

    private static char FoldChar(char c)
    {
        switch (c)
        {
            case 'ı':
            case 'İ':
            case 'I':
                return 'i';
        }

        var lower = char.ToLowerInvariant(c);
        // Emoji gibi vekil çiftlerin yarıları tek başına normalize edilemez.
        if (lower < 128 || char.IsSurrogate(lower))
        {
            return lower;
        }

        // "ç" → "c" + birleşik çengel; ilk karakter temel harftir.
        var decomposed = lower.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(decomposed[0]) != UnicodeCategory.NonSpacingMark
            ? decomposed[0]
            : lower;
    }
}

/// <summary>Bir hedef metnin aramaya hazırlanmış hali: katlanmış metin ve kelime başı bilgisi.</summary>
public sealed class FoldedText
{
    private readonly string _original;

    public FoldedText(string original)
    {
        _original = original;
        Folded = FuzzyMatcher.Fold(original);
    }

    public string Folded { get; }

    /// <summary>Metnin başı, ayraçtan sonrası ("Visual Studio", "my-app", "file_name") veya camelCase geçişi.</summary>
    public bool IsWordStart(int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = _original[index - 1];
        var current = _original[index];
        if (!char.IsLetterOrDigit(previous))
        {
            return char.IsLetterOrDigit(current);
        }

        return char.IsLower(previous) && char.IsUpper(current);
    }
}
