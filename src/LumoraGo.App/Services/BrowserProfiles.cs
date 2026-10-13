using System.IO;
using LumoraGo.Core.Plugins;

namespace LumoraGo.App.Services;

/// <summary>Chromium tabanlı tarayıcıların profil klasörlerindeki yer imi dosyaları ve Windows'un son kullanılanlar listesi.</summary>
internal static class BrowserProfiles
{
    private static readonly (string Browser, string UserData, bool Roaming)[] Browsers =
    [
        ("Chrome", @"Google\Chrome\User Data", false),
        ("Edge", @"Microsoft\Edge\User Data", false),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data", false),
        ("Vivaldi", @"Vivaldi\User Data", false),
        ("Opera", @"Opera Software\Opera Stable", true),
        ("Opera GX", @"Opera Software\Opera GX Stable", true),
    ];

    public static IEnumerable<(string Path, string Browser)> BookmarkFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var (browser, userData, isRoaming) in Browsers)
        {
            var root = Path.Combine(isRoaming ? roaming : local, userData);
            if (!Directory.Exists(root))
            {
                continue;
            }

            // Opera profili doğrudan kök klasörde; diğerlerinde "Default" ve "Profile N".
            var direct = Path.Combine(root, "Bookmarks");
            if (File.Exists(direct))
            {
                yield return (direct, browser);
            }

            IEnumerable<string> profiles;
            try
            {
                profiles = Directory.EnumerateDirectories(root)
                    .Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var profile in profiles)
            {
                var file = Path.Combine(profile, "Bookmarks");
                if (File.Exists(file))
                {
                    yield return (file, browser);
                }
            }
        }
    }

    public static IReadOnlyList<RecentItem> RecentItems()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*.lnk")
                .Select(f => new RecentItem(Path.GetFileNameWithoutExtension(f.Name), f.FullName, f.LastWriteTime))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
