namespace LumoraGo.Plugin;

/// <param name="RawText">Kutuya yazılan metnin tamamı.</param>
/// <param name="ActionKeyword">Eşleşen tetik kelimesi ("=" gibi); yoksa null.</param>
/// <param name="Search">Tetik kelimesi çıkarıldıktan sonra kalan, kırpılmış arama metni.</param>
public sealed record Query(string RawText, string? ActionKeyword, string Search)
{
    public bool IsEmpty => Search.Length == 0;
}
