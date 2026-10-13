namespace LumoraGo.Core.Network;

/// <summary>Bir ağ aracının sonuç satırı.</summary>
/// <param name="Copy">Enter ile kopyalanacak metin; null ise başlık.</param>
public sealed record NetLine(string Title, string? Subtitle = null, string? Copy = null);

/// <summary>
/// Bir ağ aracının işi. Sonuçlar geldikçe <paramref name="report"/> ile TÜM satır listesini yeniler
/// (her çağrı öncekini değiştirir); hata olursa istisna fırlatır, mesajı kullanıcıya gösterilir.
/// </summary>
public delegate Task NetLookup(string argument, Action<IReadOnlyList<NetLine>> report, CancellationToken ct);
