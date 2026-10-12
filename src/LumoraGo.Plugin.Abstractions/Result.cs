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
    /// <summary>Sonucu üreten eklentinin kimliği. Çekirdek tarafından doldurulur.</summary>
    public string PluginId { get; init; } = "";
}

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
