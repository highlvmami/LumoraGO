using System.Text.Json.Serialization;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Apps;

public enum AppKind
{
    /// <summary>Başlat menüsündeki .lnk kısayolu.</summary>
    Shortcut,

    /// <summary>Microsoft Store / UWP uygulaması (AUMID ile açılır).</summary>
    Packaged,
}

/// <summary>İndekslenmiş bir uygulama.</summary>
/// <param name="Id">Kalıcı kimlik: kısayol yolu veya AUMID. Kullanım geçmişi bununla tutulur.</param>
/// <param name="Name">Görünen ad.</param>
/// <param name="Subtitle">Hedef yol veya "Microsoft Store uygulaması".</param>
/// <param name="LaunchPath">Açılacak yol: .lnk dosyası veya "shell:AppsFolder\AUMID".</param>
/// <param name="TargetPath">Kısayolun gösterdiği exe; yoksa null. "Klasörünü aç" ve exe adıyla arama için.</param>
public sealed record AppEntry(string Id, string Name, string Subtitle, string LaunchPath, string? TargetPath, AppKind Kind)
{
    private FoldedText? _foldedName;
    private FoldedText? _foldedExe;

    [JsonIgnore]
    public FoldedText FoldedName => _foldedName ??= new FoldedText(Name);

    /// <summary>"chrome", "code" gibi exe adı; adı farklı olan uygulamaları da buldurur.</summary>
    [JsonIgnore]
    public FoldedText? FoldedExeName =>
        _foldedExe ??= TargetPath is { Length: > 0 } path ? new FoldedText(Path.GetFileNameWithoutExtension(path)) : null;
}

/// <summary>Uygulama listesini sağlayan kaynak (Windows'ta Başlat menüsü taraması).</summary>
public interface IAppSource
{
    /// <summary>Şu anki liste. Arka planda yenilendiğinde yeni bir liste atanır; okuyan taraf kilitlenmez.</summary>
    IReadOnlyList<AppEntry> Apps { get; }
}

[Flags]
public enum LaunchMode
{
    Normal = 0,
    OpenFolder = 1,
    AsAdmin = 2,
}

public interface IAppLauncher
{
    void Launch(AppEntry app, LaunchMode mode);
}
