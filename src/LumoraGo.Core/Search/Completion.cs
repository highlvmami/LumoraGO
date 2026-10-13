using LumoraGo.Plugin;

namespace LumoraGo.Core.Search;

/// <summary>Tab ile tamamlama ve kutuda soluk görünen öneri.</summary>
public static class Completion
{
    /// <summary>Seçili sonuç için Tab'ın kutuya yazacağı metin; tamamlanacak bir şey yoksa null.</summary>
    public static string? Complete(string query, Result? selected)
    {
        var text = TextOf(selected);
        return text is null || text == query ? null : text;
    }

    /// <summary>
    /// Kutudaki metin önerinin başıysa önerinin geri kalanı ("vis" yazılıyken "ual Studio Code"); değilse boş.
    /// Büyük/küçük harf fark etmez.
    /// </summary>
    public static string GhostSuffix(string query, Result? selected)
    {
        var text = TextOf(selected);
        if (query.Length == 0 || text is null || text.Length <= query.Length
            || !text.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return "";
        }

        return text[query.Length..];
    }

    private static string? TextOf(Result? selected) =>
        selected is null ? null : selected.AutoComplete ?? selected.Title;
}
