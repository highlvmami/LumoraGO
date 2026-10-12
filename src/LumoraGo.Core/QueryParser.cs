using LumoraGo.Plugin;

namespace LumoraGo.Core;

/// <summary>Kutudaki metni tetik kelimesi ve arama metni olarak ayırır.</summary>
public static class QueryParser
{
    /// <summary>
    /// İlk kelime kayıtlı bir tetik kelimesiyse onu <see cref="Query.ActionKeyword"/> olarak ayırır.
    /// Tetik kelimesinin ardından boşluk gelmesi gerekir; "=" tek başına yazıldığında da tetik sayılır.
    /// </summary>
    public static Query Parse(string? rawText, IReadOnlySet<string> actionKeywords)
    {
        var raw = rawText ?? "";
        var trimmed = raw.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var firstWord = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];

        if (firstWord.Length > 0 && actionKeywords.Contains(firstWord))
        {
            var rest = spaceIndex < 0 ? "" : trimmed[(spaceIndex + 1)..];
            return new Query(raw, firstWord, rest.Trim());
        }

        return new Query(raw, null, raw.Trim());
    }
}
