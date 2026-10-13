namespace LumoraGo.Core.DeepSearch;

/// <summary>Derin aramanın nerede ve nasıl arayacağı.</summary>
public sealed record DeepSearchOptions
{
    /// <summary>Taranacak kök klasörler (varsayılan: tüm sabit diskler).</summary>
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Bu adlardaki klasörlere girilmez (büyük/küçük harf fark etmez).</summary>
    public IReadOnlySet<string> SkipDirectoryNames { get; init; } = DefaultSkipDirectoryNames;

    /// <summary>İçerik aramasına girmeyen dosya uzantıları; adları yine aranır.</summary>
    public IReadOnlySet<string> BinaryExtensions { get; init; } = DefaultBinaryExtensions;

    /// <summary>Bundan büyük dosyaların içine bakılmaz.</summary>
    public long MaxContentBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Bu kadar sonuç bulununca arama durur.</summary>
    public int MaxHits { get; init; } = 300;

    /// <summary>Aynı anda okunan dosya sayısı.</summary>
    public int Parallelism { get; init; } = 4;

    public static readonly IReadOnlySet<string> DefaultSkipDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
        "$WinREAgent", "Recovery", "AppData", "node_modules", ".git", ".vs", "__pycache__",
    };

    public static readonly IReadOnlySet<string> DefaultBinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".cab", ".iso", ".img", ".vhd", ".vhdx", ".bin", ".dat", ".db", ".sqlite", ".pdb", ".obj", ".lib",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".heic", ".tif", ".tiff", ".psd", ".raw",
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm",
        ".zip", ".7z", ".rar", ".gz", ".tar", ".xz", ".nupkg", ".jar", ".apk",
        ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt", ".odt", ".ods",
        ".ttf", ".otf", ".woff", ".woff2", ".pak", ".lnk",
    };
}
