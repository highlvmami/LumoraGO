using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LumoraGo.Core.Desktop;
using Microsoft.Win32;

namespace LumoraGo.App.Services;

/// <summary>"/sistem", "/temizle" ve "/başlangıç" için Windows tarafı: ölçümler, temizlik hedefleri ve açılış kayıtları.</summary>
internal static class SystemManagement
{
    // ---- Sistem durumu ----

    public static async Task<SystemSnapshot> SnapshotAsync(CancellationToken ct)
    {
        // İşlemci yüzdesi iki ölçüm arasındaki farktan çıkar.
        var first = CpuTimes();
        await Task.Delay(500, ct).ConfigureAwait(false);
        var second = CpuTimes();
        var idle = second.Idle - first.Idle;
        var total = second.Kernel + second.User - first.Kernel - first.User;
        var cpu = total > 0 ? Math.Clamp((total - idle) * 100.0 / total, 0, 100) : 0;

        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref memory);

        var disks = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DiskUsage(d.Name.TrimEnd('\\'), SafeLabel(d), d.TotalSize, d.AvailableFreeSpace))
            .ToList();

        BatteryStatus? battery = null;
        if (GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100)
        {
            battery = new BatteryStatus(
                power.BatteryLifePercent,
                power.ACLineStatus == 1,
                power.BatteryLifeTime is > 0 and < 0x7FFFFFFF ? TimeSpan.FromSeconds(power.BatteryLifeTime) : null);
        }

        var top = ProcessService.List()
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RunningProcess(g.First().Id, g.Key, g.Sum(p => p.MemoryBytes), g.Select(p => p.ExePath).FirstOrDefault(p => p is not null)))
            .OrderByDescending(p => p.MemoryBytes)
            .Take(5)
            .ToList();

        return new SystemSnapshot(cpu, (long)memory.TotalPhys, (long)(memory.TotalPhys - memory.AvailPhys), disks, battery,
            TimeSpan.FromMilliseconds(Environment.TickCount64), top);
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static (long Idle, long Kernel, long User) CpuTimes() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel, user) : (0, 0, 0);

    public static void OpenTaskManager() => Start("taskmgr.exe", "");

    public static void OpenStartupSettings() => Start("ms-settings:startupapps", "");

    // ---- Temizlik ----

    public static IReadOnlyList<CleanupTarget> CleanupTargets(Action<string> openPath)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var day = TimeSpan.FromDays(1);
        var temp = new[] { Path.GetTempPath(), Path.Combine(windows, "Temp") }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var browser = BrowserCacheFolders(local);
        var crashes = new[] { Path.Combine(local, "CrashDumps"), Path.Combine(local, "Microsoft", "Windows", "WER") };
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        return
        [
            new CleanupTarget("Geçici dosyalar", "Kullanıcı ve Windows Temp klasörleri, 1 günden eski olanlar", "🗑️",
                () => FolderCleaner.Measure(temp, day), () => FolderCleaner.Clean(temp, day)),
            new CleanupTarget("Geri Dönüşüm Kutusu", "Kalıcı olarak silinir", "♻️", RecycleBinSize, EmptyRecycleBin),
            new CleanupTarget("Tarayıcı önbellekleri", "Chrome, Edge, Brave, Opera; oturumlar ve şifreler silinmez", "🌐",
                () => FolderCleaner.Measure(browser, TimeSpan.Zero), () => FolderCleaner.Clean(browser, TimeSpan.Zero)),
            new CleanupTarget("Hata raporları", "Çöken uygulamaların döküm dosyaları", "🧾",
                () => FolderCleaner.Measure(crashes, TimeSpan.Zero), () => FolderCleaner.Clean(crashes, TimeSpan.Zero)),
            new CleanupTarget("İndirilenler", "Silinmez, klasör açılır; neyin gideceğine sen karar ver", "📥",
                () => FolderCleaner.Measure([downloads], TimeSpan.Zero), null, () => openPath(downloads)),
            new CleanupTarget("Windows Disk Temizleme", "Güncelleme artıkları ve sistem dosyaları için", "🧰",
                () => 0, null, () => Start("cleanmgr.exe", "")),
        ];
    }

    private static List<string> BrowserCacheFolders(string local)
    {
        string[] roots =
        [
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
        ];
        string[] cacheNames = ["Cache", "Code Cache", "GPUCache"];
        var folders = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root))
                {
                    folders.AddRange(cacheNames.Select(n => Path.Combine(profile, n)).Where(Directory.Exists));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var opera = Path.Combine(local, "Opera Software");
        if (Directory.Exists(opera))
        {
            folders.AddRange(Directory.EnumerateDirectories(opera).SelectMany(p => cacheNames.Select(n => Path.Combine(p, n))).Where(Directory.Exists));
        }

        return folders;
    }

    private static long RecycleBinSize()
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.TotalSize : 0;
    }

    private static CleanupOutcome EmptyRecycleBin()
    {
        var size = RecycleBinSize();
        const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        return result == 0 ? new CleanupOutcome(size, 0) : new CleanupOutcome(0, 1);
    }

    // ---- Başlangıç ----

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private sealed record Source(RegistryKey Hive, string? RunPath, string? Folder, string Approved, string Location);

    private static IEnumerable<Source> Sources() =>
    [
        new(Registry.CurrentUser, RunKey, null, "Run", "Kayıt defteri (bu kullanıcı)"),
        new(Registry.LocalMachine, RunKey, null, "Run", "Kayıt defteri (tüm kullanıcılar)"),
        new(Registry.LocalMachine, RunKey32, null, "Run32", "Kayıt defteri (tüm kullanıcılar, 32 bit)"),
        new(Registry.CurrentUser, null, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "StartupFolder", "Başlangıç klasörü"),
        new(Registry.LocalMachine, null, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "StartupFolder", "Başlangıç klasörü (tüm kullanıcılar)"),
    ];

    public static IReadOnlyList<StartupItem> StartupItems()
    {
        var items = new List<StartupItem>();
        foreach (var source in Sources())
        {
            var machine = source.Hive == Registry.LocalMachine;
            foreach (var (name, command) in Entries(source))
            {
                items.Add(new StartupItem(Display(name), command, source.Location, IsEnabled(source, name), machine, ExeOf(command)));
            }
        }

        return items;
    }

    /// <returns>Hata mesajı; başarılıysa null.</returns>
    public static string? SetStartupEnabled(StartupItem item, bool enabled)
    {
        foreach (var source in Sources().Where(s => s.Location == item.Location))
        {
            var name = Entries(source).Select(e => e.Name).FirstOrDefault(n => Display(n) == item.Name);
            if (name is null)
            {
                continue;
            }

            try
            {
                using var key = source.Hive.CreateSubKey(ApprovedKey + source.Approved, writable: true);
                // Görev Yöneticisi'nin kullandığı biçim: 12 bayt, ilk bayt 2 açık / 3 kapalı, ardından kapatılma zamanı.
                var value = new byte[12];
                value[0] = enabled ? (byte)2 : (byte)3;
                if (!enabled)
                {
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
                }

                key.SetValue(name, value, RegistryValueKind.Binary);
                return null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return item.NeedsAdmin
                    ? $"{item.Name} tüm kullanıcılar için kayıtlı; yönetici izni gerekiyor. Windows ayarı açılıyor."
                    : ex.Message;
            }
        }

        return "Kayıt bulunamadı; liste yenilendi.";
    }

    private static IEnumerable<(string Name, string Command)> Entries(Source source)
    {
        if (source.RunPath is { } runPath)
        {
            using var key = source.Hive.OpenSubKey(runPath);
            if (key is null)
            {
                yield break;
            }

            foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
            {
                if (key.GetValue(name) is string command)
                {
                    yield return (name, command);
                }
            }
        }
        else if (source.Folder is { Length: > 0 } folder && Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                yield return (Path.GetFileName(file), file);
            }
        }
    }

    private static bool IsEnabled(Source source, string name)
    {
        try
        {
            using var key = source.Hive.OpenSubKey(ApprovedKey + source.Approved);
            return key?.GetValue(name) is not byte[] { Length: > 0 } value || (value[0] & 1) == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return true;
        }
    }

    private static string Display(string name) =>
        Path.GetExtension(name) is ".lnk" or ".url" ? Path.GetFileNameWithoutExtension(name) : name;

    private static string? ExeOf(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        string path;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            path = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe > 0 ? text[..(exe + 4)] : text;
        }

        return File.Exists(path) ? path : null;
    }

    private static void Start(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    // ---- Windows API ----

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalSize;
        public long NumItems;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
