using System.Globalization;
using System.Text;

namespace LumoraGo.Core.Preview;

public enum PreviewKind
{
    /// <summary>Yalnızca bilgi satırları (ve Windows küçük resmi).</summary>
    Info,

    /// <summary>Dosya olmayan sonucun metni (pano, not, yer imi).</summary>
    Note,
    Text,
    Image,

    /// <summary>PDF, Office gibi Windows önizleyicisi olan belgeler.</summary>
    Document,
    Media,
    Folder,
}

/// <param name="Text">Metin dosyasının başı, klasör listesi ya da not metni.</param>
/// <param name="Truncated">Metin ya da liste kesildiyse true.</param>
public sealed record PreviewModel(
    PreviewKind Kind,
    string Title,
    string? Path,
    IReadOnlyList<(string Label, string Value)> Details,
    string? Text = null,
    bool Truncated = false);

/// <summary>Önizleme panelinde ne gösterileceğine karar verir ve metin/klasör içeriğini okur. Çizim uygulamada yapılır.</summary>
public static class PreviewBuilder
{
    public const int MaxTextLines = 300;
    public const int MaxTextBytes = 256 * 1024;
    public const int MaxFolderItems = 50;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff", ".heic", ".jfif",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".msg", ".eml", ".vsdx",
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".yml", ".yaml", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".toml", ".env",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".xaml", ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".kt",
        ".c", ".h", ".cpp", ".hpp", ".go", ".rs", ".rb", ".php", ".html", ".htm", ".css", ".scss", ".sql", ".sh", ".ps1",
        ".bat", ".cmd", ".gitignore", ".editorconfig", ".srt", ".vtt",
    };

    // Bulut dosyası (OneDrive "yalnızca çevrimiçi"): içine bakmak indirmeyi tetikler.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static PreviewModel ForText(string title, string text) =>
        new(PreviewKind.Note, title, null, [("Uzunluk", $"{text.Length.ToString("N0", Turkish)} karakter")], text);

    public static PreviewModel ForPath(string path)
    {
        if (Directory.Exists(path))
        {
            return Folder(path);
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new PreviewModel(PreviewKind.Info, System.IO.Path.GetFileName(path), path, [("Durum", "Dosya bulunamadı"), ("Konum", path)]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new PreviewModel(PreviewKind.Info, path, path, [("Durum", ex.Message)]);
        }

        var details = new List<(string, string)>
        {
            ("Boyut", Size(info.Length)),
            ("Değiştirilme", info.LastWriteTime.ToString("d MMMM yyyy HH:mm", Turkish)),
            ("Konum", info.DirectoryName ?? ""),
        };

        var extension = info.Extension;
        if ((info.Attributes & (FileAttributes.Offline | RecallOnDataAccess)) != 0)
        {
            details.Insert(0, ("Durum", "Yalnızca çevrimiçi (indirilmesin diye içine bakılmadı)"));
            return new PreviewModel(PreviewKind.Info, info.Name, path, details);
        }

        if (ImageExtensions.Contains(extension))
        {
            return new PreviewModel(PreviewKind.Image, info.Name, path, details);
        }

        if (DocumentExtensions.Contains(extension))
        {
            return new PreviewModel(PreviewKind.Document, info.Name, path, details);
        }

        if (MediaExtensions.Contains(extension))
        {
            return new PreviewModel(PreviewKind.Media, info.Name, path, details);
        }

        if (TextExtensions.Contains(extension) || (info.Length <= 50L * 1024 * 1024 && LooksLikeText(path)))
        {
            try
            {
                var (text, truncated) = ReadHead(path);
                return new PreviewModel(PreviewKind.Text, info.Name, path, details, text, truncated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                details.Insert(0, ("Durum", $"Okunamadı: {ex.Message}"));
            }
        }

        return new PreviewModel(PreviewKind.Info, info.Name, path, details);
    }

    private static PreviewModel Folder(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (name.Length == 0)
        {
            name = path;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            var entries = directory.EnumerateFileSystemInfos("*", options)
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var folders = entries.Count(e => e is DirectoryInfo);
            var files = entries.Count - folders;
            var bytes = entries.OfType<FileInfo>().Sum(f => f.Length);
            var listed = entries.Take(MaxFolderItems).Select(e => e is DirectoryInfo ? $"📁 {e.Name}" : $"    {e.Name}");
            return new PreviewModel(
                PreviewKind.Folder,
                name,
                path,
                [
                    ("İçerik", $"{folders} klasör, {files} dosya"),
                    ("Dosyaların boyutu", Size(bytes)),
                    ("Değiştirilme", directory.LastWriteTime.ToString("d MMMM yyyy HH:mm", Turkish)),
                ],
                string.Join("\n", listed),
                entries.Count > MaxFolderItems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PreviewModel(PreviewKind.Info, name, path, [("Durum", $"Klasör okunamadı: {ex.Message}")]);
        }
    }

    internal static (string Text, bool Truncated) ReadHead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        var lines = 0;
        while (reader.ReadLine() is { } line)
        {
            if (lines == MaxTextLines || builder.Length + line.Length > MaxTextBytes)
            {
                return (builder.ToString().TrimEnd('\n'), true);
            }

            builder.Append(line).Append('\n');
            lines++;
        }

        return (builder.ToString().TrimEnd('\n'), false);
    }

    /// <summary>İlk 4 KB'ta NUL baytı yoksa metin sayılır.</summary>
    internal static bool LooksLikeText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[4096];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
            {
                return true; // UTF-16 BOM
            }

            return Array.IndexOf(buffer, (byte)0, 0, read) < 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bayt",
        < 1024 * 1024 => $"{(bytes / 1024d).ToString("0.#", Turkish)} KB",
        < 1024L * 1024 * 1024 => $"{(bytes / 1024d / 1024).ToString("0.#", Turkish)} MB",
        _ => $"{(bytes / 1024d / 1024 / 1024).ToString("0.##", Turkish)} GB",
    };
}
