namespace LumoraGo.Core.Desktop;

/// <param name="FreedBytes">Silinen dosyaların toplam boyutu.</param>
/// <param name="Skipped">Kullanımda olduğu ya da izin olmadığı için silinemeyen dosya sayısı.</param>
public sealed record CleanupOutcome(long FreedBytes, int Skipped);

/// <summary>"/temizle" satırı: ne kadar yer kapladığını ölçer, Enter'da temizler ya da (İndirilenler gibi) yalnızca açar.</summary>
/// <param name="Clean">null ise satır silmez, <paramref name="Open"/> ile klasörü açar.</param>
public sealed record CleanupTarget(
    string Name,
    string Hint,
    string Emoji,
    Func<long> Measure,
    Func<CleanupOutcome>? Clean,
    Action? Open = null);

/// <summary>Klasör içindeki dosyaları ölçer ve siler. Kullanımdaki ya da izinsiz dosyalar atlanır, klasörün kendisi kalır.</summary>
public static class FolderCleaner
{
    /// <param name="minAge">Bundan yeni dosyalara dokunulmaz (çalışan bir kurulumun geçici dosyası silinmesin).</param>
    public static long Measure(IEnumerable<string> folders, TimeSpan minAge, DateTime? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow) - minAge;
        return folders.SelectMany(Files).Where(f => Old(f, cutoff)).Sum(Length);
    }

    public static CleanupOutcome Clean(IEnumerable<string> folders, TimeSpan minAge, DateTime? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow) - minAge;
        long freed = 0;
        var skipped = 0;
        foreach (var folder in folders)
        {
            // Klasör tarihleri içindeki dosya silinince değişir; eski olanları silmeden önce belirle.
            var oldFolders = Directories(folder).Where(d => d.LastWriteTimeUtc < cutoff).ToList();
            foreach (var file in Files(folder).Where(f => Old(f, cutoff)).ToList())
            {
                var length = Length(file);
                try
                {
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }

            RemoveEmptyFolders(oldFolders);
        }

        return new CleanupOutcome(freed, skipped);
    }

    private static IEnumerable<FileInfo> Files(string folder)
    {
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists)
        {
            return [];
        }

        return directory.EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        });
    }

    private static IEnumerable<DirectoryInfo> Directories(string folder)
    {
        var root = new DirectoryInfo(folder);
        return root.Exists
            ? root.EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            : [];
    }

    private static void RemoveEmptyFolders(IEnumerable<DirectoryInfo> folders)
    {
        // En derindekinden başla ki boşalan üst klasör de silinebilsin.
        foreach (var dir in folders.OrderByDescending(d => d.FullName.Length))
        {
            try
            {
                if (!dir.EnumerateFileSystemInfos().Any())
                {
                    dir.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kullanımda; kalsın.
            }
        }
    }

    private static bool Old(FileInfo file, DateTime cutoff)
    {
        try
        {
            return file.LastWriteTimeUtc < cutoff;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static long Length(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <param name="Location">Kullanıcıya görünen yer, örn. "Kayıt defteri (kullanıcı)" ya da "Başlangıç klasörü".</param>
/// <param name="NeedsAdmin">Tüm kullanıcılar için kayıtlı; açıp kapatmak yönetici izni ister.</param>
public sealed record StartupItem(string Name, string Command, string Location, bool Enabled, bool NeedsAdmin, string? ExePath);
