using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using LumoraGo.App.Services;
using LumoraGo.App.ViewModels;
using LumoraGo.Core;
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

        var plugins = new PluginManager(_log);
        var search = new SearchService(plugins, _log);
        _window = new MainWindow(new MainViewModel(search, _log), _log);
        _tray = new TrayIcon(() => _window.ShowLauncher(), OpenLogFolder, ExitApplication);

        _updates = new UpdateService(_log);
        var updates = _updates;
        var releaseNotes = ReadEmbeddedChangelog();

        var host = new LauncherHost(_window, _tray, _log);
        var commands = BuildCommands(host);
        plugins.Initialize(
            [
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
            _tray.ShowBalloon("LumoraGo güncellendi", $"Sürüm {updatedTo}. Neler değişti: /version");
        }

        _ = CheckForUpdatesAsync();

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
                });
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
        if (_updates is null || _window is null)
        {
            return;
        }

        await _updates.CheckAndDownloadAsync();
        if (!_updates.IsReadyToApply)
        {
            return;
        }

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
