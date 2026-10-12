using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LumoraGo.Core;
using LumoraGo.Core.Apps;

namespace LumoraGo.App.Services;

/// <summary>
/// Başlat menüsü kısayollarını (.lnk, kullanıcı + ortak) ve Microsoft Store uygulamalarını indeksler.
/// Açılışta önce diskteki önbellek yüklenir (anında arama), sonra arka planda yeniden taranır.
/// </summary>
internal sealed partial class WindowsAppSource : IAppSource
{
    private readonly string _cachePath;
    private readonly StaWorker _worker;
    private readonly ILog _log;
    private volatile IReadOnlyList<AppEntry> _apps = [];

    public WindowsAppSource(string cachePath, StaWorker worker, ILog log)
    {
        _cachePath = cachePath;
        _worker = worker;
        _log = log;
    }

    public IReadOnlyList<AppEntry> Apps => _apps;

    public void Start()
    {
        LoadCache();
        _worker.Post(Refresh);
    }

    [GeneratedRegex(@"uninstall|kaldır|readme|beni oku|help|yardım|documentation|website|release notes", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseNames();

    private void Refresh()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var shortcuts = ScanShortcuts();
            var names = shortcuts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packaged = ScanPackaged().Where(a => names.Add(a.Name));

            var apps = shortcuts.Concat(packaged)
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _apps = apps;
            SaveCache(apps);
            IconCache.Warm(apps.Select(a => a.LaunchPath));
            _log.Info($"Uygulama indeksi: {apps.Count} uygulama, {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            _log.Error($"Uygulama indekslenemedi: {ex}");
        }
    }

    private List<AppEntry> ScanShortcuts()
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var shell = CreateComObject("WScript.Shell");
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk", options))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (NoiseNames().IsMatch(name) || !seen.Add(name))
                {
                    continue;
                }

                var target = ResolveTarget(shell, lnk);
                var subtitle = target ?? Path.GetRelativePath(folder, Path.GetDirectoryName(lnk)!);
                result.Add(new AppEntry(lnk, name, subtitle, lnk, target, AppKind.Shortcut));
            }
        }

        return result;
    }

    private string? ResolveTarget(dynamic? shell, string lnk)
    {
        if (shell is null)
        {
            return null;
        }

        try
        {
            string target = shell.CreateShortcut(lnk).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex)
        {
            _log.Debug($"Kısayol çözülemedi: {lnk}: {ex.Message}");
            return null;
        }
    }

    /// <summary>shell:AppsFolder içinde AUMID'i olanlar ("Paket!Uygulama") Store uygulamalarıdır.</summary>
    private List<AppEntry> ScanPackaged()
    {
        var result = new List<AppEntry>();
        var shell = CreateComObject("Shell.Application");
        if (shell is null)
        {
            return result;
        }

        try
        {
            var items = shell.NameSpace("shell:AppsFolder").Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                var item = items.Item(i);
                string aumid = item.Path;
                string name = item.Name;
                if (string.IsNullOrEmpty(aumid) || !aumid.Contains('!') || aumid.Contains('\\') || NoiseNames().IsMatch(name))
                {
                    continue;
                }

                result.Add(new AppEntry(aumid, name, "Microsoft Store uygulaması", @"shell:AppsFolder\" + aumid, null, AppKind.Packaged));
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Store uygulamaları okunamadı: {ex.Message}");
        }

        return result;
    }

    private dynamic? CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId);
        if (type is null)
        {
            _log.Warn($"COM nesnesi bulunamadı: {progId}");
            return null;
        }

        return Activator.CreateInstance(type);
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                _apps = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(_cachePath)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.Warn($"Uygulama önbelleği okunamadı: {ex.Message}");
        }
    }

    private void SaveCache(List<AppEntry> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(apps));
        }
        catch (IOException ex)
        {
            _log.Warn($"Uygulama önbelleği yazılamadı: {ex.Message}");
        }
    }
}
