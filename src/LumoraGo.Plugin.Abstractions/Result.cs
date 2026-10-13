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

    /// <summary>
    /// Satır fareyle tutulup başka bir pencereye bırakılınca giden şey: dosya, adres ya da metin. Boşsa ve
    /// <see cref="PreviewText"/> doluysa o metin gider; ikisi de yoksa satır sürüklenmez.
    /// </summary>
    public DragPayload? DragData { get; init; }

    /// <summary>Sonucu üreten eklentinin kimliği. Çekirdek tarafından doldurulur.</summary>
    public string PluginId { get; init; } = "";
}

public enum DragKind
{
    /// <summary>Gerçek dosya ya da klasör: Gezgin'e kopyalanır, sohbet uygulamalarına ek olur.</summary>
    File,

    /// <summary>Bağlantı: tarayıcıya ya da metin kutusuna adres olarak.</summary>
    Url,

    /// <summary>Düz metin.</summary>
    Text,
}

/// <summary>Sürükle-bırak ile giden veri.</summary>
public sealed record DragPayload(DragKind Kind, string Value)
{
    public static DragPayload File(string path) => new(DragKind.File, path);

    public static DragPayload Url(string url) => new(DragKind.Url, url);

    public static DragPayload Text(string text) => new(DragKind.Text, text);

    /// <summary>Satırın sürüklenince taşıyacağı veri; yoksa null.</summary>
    public static DragPayload? For(Result result) =>
        result.DragData ?? (result.PreviewText is { Length: > 0 } text ? Text(text) : null);
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
