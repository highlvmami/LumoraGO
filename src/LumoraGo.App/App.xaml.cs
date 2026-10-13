using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LumoraGo.App.Services;
using LumoraGo.App.ViewModels;
using LumoraGo.Core;
using LumoraGo.Core.Apps;
using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Files;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.App;

public partial class App : Application
{
    private const string MutexName = @"Local\LumoraGo.SingleInstance";
    private const string ShowEventName = @"Local\LumoraGo.Show";

    private static readonly string DataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumoraGo");

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private FileLog? _log;
    private HotkeyService? _hotkey;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private UpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private FileIndex? _files;
    private Timer? _fileIndexTimer;

    private static readonly TimeSpan FileIndexFirstBuild = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FileIndexInterval = TimeSpan.FromMinutes(30);
    private bool _applyScheduled;

    // Açık kalan launcher da yeni sürümü bulsun; GitHub'ın kimliksiz istek sınırının çok altında.
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(15);
    private ThemeService? _theme;
    private StaWorker? _shellWorker;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Tek örnek: ikinci kopya açılırsa çalışan kopyaya "göster" sinyali gönderip kapanır.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalRunningInstance();
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var startup = Stopwatch.StartNew();

        _log = new FileLog(Path.Combine(DataRoot, "logs"));
        _log.Info($"LumoraGo {typeof(App).Assembly.GetName().Version} başlıyor");
        HookUnhandledExceptions(_log);

        // Varsayılan görünüm koyu; Windows açık temada olsa da değişmez.
        _theme = new ThemeService(this, WindowEffects.SupportsBackdrop, followSystem: false);
        _shellWorker = new StaWorker("LumoraGo.Shell");
        IconCache.Initialize(_shellWorker);
        var apps = new WindowsAppSource(Path.Combine(DataRoot, "cache", "apps.json"), _shellWorker, _log);
        apps.Start();
        var usage = new UsageStore(Path.Combine(DataRoot, "usage.json"), _log);

        _files = new FileIndex(UserFolders, DeepSearchOptions.DefaultSkipDirectoryNames, _log);
        _fileIndexTimer = new Timer(_ => _files.Rebuild(), null, FileIndexFirstBuild, FileIndexInterval);

        var plugins = new PluginManager(_log);
        var search = new SearchService(plugins, _log);
        _window = new MainWindow(new MainViewModel(search, _log), _log, _theme);
        _tray = new TrayIcon(() => _window.ShowLauncher(), OpenLogFolder, ExitApplication);

        _updates = new UpdateService(_log);
        var updates = _updates;
        var releaseNotes = ReadEmbeddedChangelog();

        var host = new LauncherHost(_window, _tray, _log);
        var commands = BuildCommands(host);
        plugins.Initialize(
            [
                new PluginEntry(CalculatorPlugin.Metadata, new CalculatorPlugin()),
                new PluginEntry(AppsPlugin.Metadata, new AppsPlugin(apps, new ShellAppLauncher(), usage)),
                WebSearchEntry(WebSearchPlugin.YouTube(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.Google(OpenUrl)),
                LinkEntry(LinkPlugin.GitHub(OpenUrl)),
                LinkEntry(LinkPlugin.LinkedIn(OpenUrl)),
                LinkEntry(LinkPlugin.LinkedIn(OpenUrl, misspelled: true)),
                new PluginEntry(FileSearchPlugin.Metadata, new FileSearchPlugin(_files, OpenPath, RevealPath)),
                new PluginEntry(DeepSearchPlugin.Metadata, new DeepSearchPlugin(
                    () => new DeepSearchOptions { Roots = FixedDrives() }, OpenPath, RevealPath)),
                .. SystemShortcuts(host).Select(p => new PluginEntry(p.Metadata, p)),
                new PluginEntry(KeywordHintsPlugin.Metadata, new KeywordHintsPlugin(() => plugins.Plugins.Select(p => p.Metadata))),
                new PluginEntry(CommandsPlugin.Metadata, new CommandsPlugin(commands)),
                new PluginEntry(CommandListPlugin.Metadata, new CommandListPlugin(() => ListCommands(commands, plugins, host))),
                new PluginEntry(VersionPlugin.Metadata, new VersionPlugin(() => new VersionInfo(
                    updates.CurrentVersion,
                    Changelog.GetNotes(releaseNotes, updates.CurrentVersion),
                    updates.Status))),
            ],
            host,
            Path.Combine(DataRoot, "plugins"));

        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => _window.ToggleLauncher(Stopwatch.StartNew());
        if (!_hotkey.Register(ModifierKeys.Alt, Key.Space))
        {
            _log.Warn("Alt+Space kaydedilemedi; başka bir uygulama kullanıyor olabilir.");
            _tray.ShowBalloon(
                "Kısayol alınamadı",
                "Alt+Space başka bir uygulama tarafından kullanılıyor. Şimdilik tepsi simgesine tıklayarak açabilirsin.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _window.ShowLauncher()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _log.Info($"Hazır: {startup.ElapsedMilliseconds} ms");

        if (Program.RestartedAfterUpdate is { } updatedTo)
        {
            _tray.ShowBalloon("LumoraGo güncellendi", $"Sürüm {updatedTo}. Neler değişti: /v");
        }

        _ = CheckForUpdatesAsync();
        _updateTimer = new DispatcherTimer { Interval = UpdateInterval };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateTimer.Start();

        // Windows açılışında --background ile başlar ve sessizce tepside bekler.
        if (!e.Args.Contains(StartupRegistration.BackgroundArg))
        {
            _tray.ShowBalloon("LumoraGo çalışıyor", "Alt+Space ile aç, Esc ile gizle.");
            _window.ShowLauncher();
        }
    }

    /// <summary>"/liste" için: LumoraGo komutları ve tetik kelimesi olan eklentiler.</summary>
    private static IEnumerable<CommandInfo> ListCommands(
        IReadOnlyList<LauncherCommand> commands, PluginManager plugins, IPluginHost host)
    {
        foreach (var command in commands)
        {
            yield return new CommandInfo(command.Title, command.Subtitle, command.Action);
        }

        foreach (var plugin in plugins.Plugins)
        {
            if (plugin.Metadata.ActionKeyword is { } keyword && keyword != CommandListPlugin.Keyword)
            {
                yield return new CommandInfo($"{plugin.Metadata.Name} ({keyword})", $"\"{keyword} \" yazarak kullan", _ =>
                {
                    host.ChangeQuery(keyword + " ");
                    return false;
                }, keyword + " ");
            }
        }
    }

    private IReadOnlyList<LauncherCommand> BuildCommands(IPluginHost host) =>
    [
        new("Tüm komutları listele", $"Kısayolu: {CommandListPlugin.Keyword}", _ =>
        {
            host.ChangeQuery(CommandListPlugin.Keyword);
            return false;
        }),
        new("LumoraGo'dan çık", "Launcher'ı tamamen kapatır", _ =>
        {
            Dispatcher.BeginInvoke(ExitApplication);
            return true;
        }),
        new("LumoraGo günlük klasörünü aç", "Hata ve performans kayıtları", _ =>
        {
            OpenLogFolder();
            return true;
        }),
        new("Windows açılışında başlatmayı aç/kapat", "LumoraGo'yu oturum açınca otomatik başlatır", _ =>
        {
            var enable = !StartupRegistration.IsEnabled;
            StartupRegistration.SetEnabled(enable);
            host.ShowMessage("LumoraGo", enable ? "Windows açılışında başlayacak." : "Artık otomatik başlamayacak.");
            return true;
        }),
    ];

    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null || _window is null || _applyScheduled)
        {
            return;
        }

        await _updates.CheckAndDownloadAsync();
        if (!_updates.IsReadyToApply)
        {
            return;
        }

        _applyScheduled = true;
        _updateTimer?.Stop();

        // Kullanıcı yazarken yeniden başlama; pencere gizliyse hemen, değilse gizlendiği an kur.
        if (!_window.IsVisible)
        {
            _updates.ApplyAndRestart();
            return;
        }

        _window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _updates.ApplyAndRestart();
            }
        };
    }

    private static string ReadEmbeddedChangelog()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
        {
            return "";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static PluginEntry WebSearchEntry(WebSearchPlugin plugin) => new(plugin.Metadata, plugin);

    private static PluginEntry LinkEntry(LinkPlugin plugin) => new(plugin.Metadata, plugin);

    /// <summary>Windows denetim kısayolları: "/kilit", "/kapat" gibi.</summary>
    private IEnumerable<ShortcutPlugin> SystemShortcuts(IPluginHost host) =>
    [
        new("/kilit", "Bilgisayarı kilitle", "Oturum açık kalır, şifreyle geri dönülür", () => SystemActions.Lock()),
        new("/uyku", "Uyku moduna al", "Bilgisayar uyur, tuşa basınca kaldığı yerden devam eder", () => SystemActions.Sleep()),
        new("/kapat", "Bilgisayarı kapat", "Açık uygulamalar kapanır", () => Run("shutdown", "/s /t 0")),
        new("/yeniden", "Bilgisayarı yeniden başlat", "Açık uygulamalar kapanır", () => Run("shutdown", "/r /t 0")),
        new("/oturum", "Oturumu kapat", "Kullanıcı oturumundan çıkar", () => Run("shutdown", "/l")),
        new("/cop", "Geri dönüşüm kutusunu boşalt", "Windows önce onay sorar", () => SystemActions.EmptyRecycleBin()),
        new("/ayarlar", "Windows Ayarları'nı aç", "Ayarlar uygulaması", () => OpenUrl("ms-settings:")),
        new("/gorev", "Görev Yöneticisi'ni aç", "Çalışan uygulamalar ve performans", () => Run("taskmgr", "")),
        new("/cmd", "Komut istemini aç", "cmd.exe", () => Run("cmd", "")),
        new("/ip", "IP adresini kopyala", "Yerel ağdaki IPv4 adresini panoya alır", () =>
        {
            var ip = SystemActions.LocalIPv4();
            if (ip is null)
            {
                host.ShowMessage("IP adresi", "Bağlı bir ağ bulunamadı.");
                return;
            }

            host.CopyToClipboard(ip);
            host.ShowMessage("IP adresi kopyalandı", ip);
        }),
    ];

    /// <summary>"/f" için: masaüstü, belgeler, indirilenler, resimler, videolar, müzik ve OneDrive.</summary>
    private static IReadOnlyList<string> UserFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string?[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetEnvironmentVariable("OneDrive"),
        ];

        // OneDrive belgeleri de içeriyorsa aynı dosyalar iki kez listelenmesin.
        var existing = folders.OfType<string>().Where(f => f.Length > 0 && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return existing.Where(f => !existing.Any(other => other != f && f.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>"/ds" için: tüm sabit diskler (C:\, D:\...).</summary>
    private static IReadOnlyList<string> FixedDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();

    private void OpenPath(string path) => Run(path, "");

    private void RevealPath(string path) => Run("explorer.exe", $"/select,\"{path}\"");

    private void Run(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Warn($"Çalıştırılamadı: {file} {arguments}: {ex.Message}");
        }
    }

    /// <summary>Adresi varsayılan tarayıcıda açar.</summary>
    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Warn($"Tarayıcı açılamadı: {url}: {ex.Message}");
        }
    }

    private void OpenLogFolder()
    {
        if (_log is not null)
        {
            Process.Start(new ProcessStartInfo(_log.Directory) { UseShellExecute = true });
        }
    }

    private void ExitApplication()
    {
        _window?.AllowClose();
        Shutdown();
    }

    private static void SignalRunningInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Çalışan kopya henüz olayı oluşturmadıysa yapacak bir şey yok.
        }
    }

    private void HookUnhandledExceptions(ILog log)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error($"UI thread'inde yakalanmamış hata: {args.Exception}");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error($"Yakalanmamış hata: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error($"Gözlenmemiş görev hatası: {args.Exception}");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("LumoraGo kapanıyor");
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _hotkey?.Dispose();
        _theme?.Dispose();
        _shellWorker?.Dispose();
        _tray?.Dispose();
        _log?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
