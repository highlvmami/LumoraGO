namespace LumoraGo.Plugin;

/// <summary>Bir eklentinin döndürdüğü tek sonuç. Eylemini kendisi taşır, böylece çekirdek eklentinin ne yaptığını bilmez.</summary>
/// <param name="Title">Listede görünen başlık.</param>
/// <param name="Subtitle">Başlığın altındaki açıklama.</param>
/// <param name="IconPath">Simge dosyası veya çalıştırılabilir dosya yolu.</param>
/// <param name="Score">Sıralamaya katkı; yüksek olan üstte görünür.</param>
/// <param name="Action">Enter'a basıldığında çalışır. true dönerse pencere gizlenir.</param>
/// <param name="ContextMenu">Ek eylemler (Faz 5'te sağ ok ile açılacak).</param>
public sealed record Result(
    string Title,
    string? Subtitle,
    string? IconPath,
    int Score,
    Func<ActionContext, bool> Action,
    IReadOnlyList<ContextAction>? ContextMenu = null)
{
    /// <summary>
    /// Tab'a basınca kutuya yazılacak metin. Boşsa <see cref="Title"/> kullanılır. Kutudaki metin bununla başlıyorsa
    /// kalan kısmı soluk renkte öneri olarak görünür.
    /// </summary>
    public string? AutoComplete { get; init; }

    /// <summary>Doluysa sonuç düz satır yerine büyük bilgi kartı olarak çizilir (hava, hisse, maç).</summary>
    public ResultCard? Card { get; init; }

    /// <summary>Hızlı önizlemede (Shift+Boşluk) gösterilecek dosya ya da klasör.</summary>
    public string? PreviewPath { get; init; }

    /// <summary>Dosya değilse hızlı önizlemede gösterilecek metin (pano kaydının tamamı, not, yer imi adresi).</summary>
    public string? PreviewText { get; init; }

    /// <summary>Sonucu üreten eklentinin kimliği. Çekirdek tarafından doldurulur.</summary>
    public string PluginId { get; init; } = "";
}

/// <summary>Bilgi kartı: büyük başlık değeri, altında açıklama ve satırlar, isteğe bağlı küçük eğri.</summary>
/// <param name="Headline">Büyük yazılan değer, örn. "18°" ya da "312,50 ₺".</param>
/// <param name="Caption">Değerin hemen altındaki satır, örn. "Parçalı bulutlu" ya da "+%2,35".</param>
/// <param name="Lines">Ek bilgi satırları.</param>
/// <param name="Trend">Sağda çizilecek küçük eğrinin değerleri (soldan sağa).</param>
/// <param name="Positive">true yeşil, false kırmızı, null nötr renk (değişim yönü).</param>
public sealed record ResultCard(
    string Headline,
    string? Caption = null,
    IReadOnlyList<string>? Lines = null,
    IReadOnlyList<double>? Trend = null,
    bool? Positive = null);

public sealed record ContextAction(string Title, Func<ActionContext, bool> Action);

/// <summary>Eylem çalıştırılırken basılı olan değiştirici tuşlar.</summary>
[Flags]
public enum ActionModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

public sealed record ActionContext(ActionModifiers Modifiers);
