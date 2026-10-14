using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LumoraGo.App.Services;
using LumoraGo.App.ViewModels;
using LumoraGo.Core;
using LumoraGo.Core.Actions;
using LumoraGo.Core.Apps;
using LumoraGo.Core.Browser;
using LumoraGo.Core.Clipboard;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Desktop;
using LumoraGo.Core.Extensions;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Calendar;
using LumoraGo.Core.Info;
using LumoraGo.Core.Mail;
using LumoraGo.Core.Network;
using LumoraGo.Core.Files;
using LumoraGo.Core.Notes;
using LumoraGo.Core.Google;
using LumoraGo.Core.Photos;
using LumoraGo.Core.Plugins;
using LumoraGo.Core.Scripts;
using LumoraGo.Core.Search;
using LumoraGo.Core.Settings;
using LumoraGo.Core.Timers;
using LumoraGo.Plugin;

namespace LumoraGo.App;

public partial class App : Application
{
    private const string MutexName = @"Local\LumoraGo.SingleInstance";
    private const string ShowEventName = @"Local\LumoraGo.Show";

    private static readonly string DataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumoraGo");

    /// <summary>Kullanıcının kurduğu dış eklentiler; her biri kendi alt klasöründe plugin.json ile.</summary>
    private static readonly string ExtensionsRoot = Path.Combine(DataRoot, "extensions");

    /// <summary>Adıyla çalıştırılabilen betiklerin klasörü.</summary>
    private static readonly string ScriptsRoot = Path.Combine(DataRoot, "scripts");

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Hız testi 10 sn'den uzun sürebilir; iptal belirteciyle yönetilir, HttpClient zaman aşımı yok.</summary>
    private static readonly System.Net.Http.HttpClient LongHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

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
    private SettingsStore? _settings;
    private SecretStore? _secrets;
    private ContactBook? _contacts;
    private ReminderStore? _reminders;
    private Timer? _reminderTimer;
    private GoogleContactSync? _googleContacts;
    private Timer? _googleContactsTimer;
    private static readonly TimeSpan ReminderFirstCheck = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan GoogleContactsFirstSync = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GoogleContactsInterval = TimeSpan.FromHours(3);
    private ContactsWatcher? _contactsWatcher;
    private QueryLearning? _learning;
    private SettingsWindow? _settingsWindow;
    private ClipboardHistory? _clipboard;
    private ClipboardMonitor? _clipboardMonitor;
    private ClipboardPersistence? _clipboardPersistence;
    private TimerService? _timers;

    // Launcher gizlendikten sonra önceki pencerenin odağı geri alması için beklenen süre.
    private static readonly TimeSpan PasteDelay = TimeSpan.FromMilliseconds(120);
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
        _settings = new SettingsStore(Path.Combine(DataRoot, "settings.json"), _log);
        _secrets = new SecretStore(Path.Combine(DataRoot, "accounts.dat"), SecretProtection.Protect, SecretProtection.Unprotect, _log);
        var secrets = _secrets;
        _contacts = new ContactBook(Path.Combine(DataRoot, "contacts.dat"), SecretProtection.Protect, SecretProtection.Unprotect, _log);
        var contacts = _contacts;
        _contactsWatcher = new ContactsWatcher(contacts, _log);
        _contactsWatcher.Watch(_settings.Current.ContactsFile, _settings.Current.ContactsWatch);
        _googleContacts = new GoogleContactSync(Http, secrets, contacts, _log);
        _googleContactsTimer = new Timer(_ => _ = SyncGoogleContactsAsync(), null, GoogleContactsFirstSync, GoogleContactsInterval);
        var photos = new PhotoLibrary(Path.Combine(DataRoot, "photos", "google"), () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), _log);
        var photoImport = new GooglePhotoImport(Http, secrets, photos, OpenUrl, _log);
        var google = new GoogleApi(Http, secrets);
        var googleCalendar = new GoogleCalendar(google);
        var googleTasks = new GoogleTasks(google);
        var youtube = new YouTubeLibrary(google, Path.Combine(DataRoot, "cache", "youtube.json"), _log);
        _theme = new ThemeService(this, WindowEffects.SupportsBackdrop, _settings.Current.Theme);
        _shellWorker = new StaWorker("LumoraGo.Shell");
        IconCache.Initialize(_shellWorker);
        var apps = new WindowsAppSource(Path.Combine(DataRoot, "cache", "apps.json"), _shellWorker, _log);
        apps.Start();
        var usage = new UsageStore(Path.Combine(DataRoot, "usage.json"), _log);

        _files = new FileIndex(FileSearchFolders, DeepSearchOptions.DefaultSkipDirectoryNames, _log);
        _fileIndexTimer = new Timer(_ => _files.Rebuild(), null, FileIndexFirstBuild, FileIndexInterval);

        _clipboard = new ClipboardHistory();
        if (_settings.Current.ClipboardHistory)
        {
            _clipboardMonitor = new ClipboardMonitor(_clipboard, _log);
        }

        ApplyClipboardPersistence(_settings.Current);

        _timers = new TimerService();
        _timers.Elapsed += timer => Dispatcher.BeginInvoke(() => TimerElapsed(timer));

        _settings.Changed += (_, settings) => ApplySettings(settings);

        var plugins = new PluginManager(_log);
        _learning = new QueryLearning(Path.Combine(DataRoot, "learned.json"), _log);
        var search = new SearchService(plugins, _log, learning: _learning);
        _window = new MainWindow(new MainViewModel(search, _log), _log, _theme) { PreviewAlwaysOpen = _settings.Current.PreviewAlwaysOpen };
        _tray = new TrayIcon(() => _window.ShowLauncher(), OpenSettings, OpenLogFolder, ExitApplication);

        _updates = new UpdateService(_log);
        var updates = _updates;
        _window.VersionText = updates.CurrentVersion;
        var releaseNotes = ReadEmbeddedChangelog();

        var host = new LauncherHost(_window, _tray, _log);
        var commands = BuildCommands(host);
        var media = new MediaControl(_log);
        var appLauncher = new ShellAppLauncher();
        var scripts = new ScriptCatalog(() => ScriptsRoot);
        try
        {
            scripts.EnsureFolder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Betik klasörü oluşturulamadı: {ex.Message}");
        }

        var notes = new NotesStore(Path.Combine(DataRoot, "notes.json"), _log);
        _reminders = new ReminderStore(Path.Combine(DataRoot, "reminders.json"), _log);
        var reminders = _reminders;
        _reminderTimer = new Timer(_ => CheckReminders(), null, ReminderFirstCheck, ReminderInterval);

        var scriptRunner = new ScriptRunner();
        var scriptHost = new ScriptHost(
            scriptRunner.RunAsync,
            ScriptRunner.Launch,
            () => ChainActionsFor(host, apps, appLauncher, media, scripts, scriptRunner),
            OpenPath);
        plugins.Initialize(
            [
                new PluginEntry(CalculatorPlugin.Metadata, new CalculatorPlugin()),
                new PluginEntry(ConverterPlugin.Metadata, new ConverterPlugin(new CurrencyRates(
                    Path.Combine(DataRoot, "cache", "rates.json"),
                    ct => Http.GetStringAsync(CurrencyRates.SourceUrl, ct),
                    _log))),
                new PluginEntry(ColorPlugin.Metadata, new ColorPlugin()),
                new PluginEntry(AppsPlugin.Metadata, new AppsPlugin(apps, appLauncher, usage)),
                WebSearchEntry(WebSearchPlugin.YouTube(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.Google(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.Translate(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.Wikipedia(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.Maps(OpenUrl)),
                WebSearchEntry(WebSearchPlugin.GitHubSearch(OpenUrl)),
                new PluginEntry(CustomShortcutsPlugin.Metadata, new CustomShortcutsPlugin(
                    () => _settings.Current.CustomShortcuts, OpenUrl)),
                new PluginEntry(DirectActionsPlugin.Metadata, new DirectActionsPlugin(reminders, contacts, notes, OpenUrl, null, googleCalendar, googleTasks)),
                RemindersEntry(new RemindersPlugin(reminders, "/hatırlatıcılar")),
                RemindersEntry(new RemindersPlugin(reminders, "/hatirlaticilar")),
                ContactsEntry(new ContactsPlugin(contacts, OpenUrl, () => _settings.Current.ContactsInGlobalSearch)),
                ContactsEntry(new ContactsPlugin(contacts, OpenUrl, () => true, () => Dispatcher.BeginInvoke(OpenSettings), ContactsPlugin.Keyword)),
                ContactsEntry(new ContactsPlugin(contacts, OpenUrl, () => true, () => Dispatcher.BeginInvoke(OpenSettings), "/kişi")),
                ContactsEntry(new ContactsPlugin(contacts, OpenUrl, () => true, () => Dispatcher.BeginInvoke(OpenSettings), "/rehber")),
                .. new[] { PhotosPlugin.Keyword, "/fotoğraf", "/fotograf" }.Select(k => PhotosEntry(new PhotosPlugin(
                    photos, photoImport, () => secrets.Current.Google is { RefreshToken.Length: > 0 }, OpenPath, RevealPath,
                    () => Dispatcher.BeginInvoke(OpenSettings), k))),
                ScriptsEntry(new ScriptsPlugin(scripts, Chains, scriptHost)),
                ScriptsEntry(new ScriptsPlugin(scripts, Chains, scriptHost, ScriptsPlugin.RunKeyword)),
                ScriptsEntry(new ScriptsPlugin(scripts, Chains, scriptHost, "/çalıştır")),
                .. ToolPlugin.All().Select(t => new PluginEntry(t.Metadata, t)),
                new PluginEntry(ProcessKillPlugin.Metadata, new ProcessKillPlugin(ProcessService.List, ProcessService.Kill)),
                new PluginEntry(MediaPlugin.Metadata, new MediaPlugin(media)),
                .. NetworkToolPlugins.Create(
                    new NetworkTools(Http, SystemNetwork.PingAsync, SystemNetwork.WhoisAsync),
                    new SpeedTest(LongHttp)).Select(p => new PluginEntry(p.Metadata, p)),
                new PluginEntry(CalendarPlugin.Metadata, new CalendarPlugin(
                    new CombinedCalendarSource(new CalendarSource(Http, () => secrets.Current.CalendarUrls), googleCalendar),
                    OpenUrl, () => Dispatcher.BeginInvoke(OpenSettings))),
                .. new[] { TasksPlugin.Keyword, "/görevler", "/yapılacak", "/yapilacak", "/todo" }.Select(k => TasksEntry(new TasksPlugin(
                    googleTasks, OpenUrl, () => Dispatcher.BeginInvoke(OpenSettings), k))),
                DriveEntry(DriveSearch.Create(new GoogleDrive(google), () => google.IsConnected, OpenUrl, () => Dispatcher.BeginInvoke(OpenSettings))),
                new PluginEntry(YouTubePlugin.Metadata, new YouTubePlugin(youtube, OpenUrl, () => Dispatcher.BeginInvoke(OpenSettings))),
                new PluginEntry(MailPlugin.Metadata, new MailPlugin(
                    () => secrets.Current.Mail, ImapClient.ConnectTlsAsync, OpenUrl, () => Dispatcher.BeginInvoke(OpenSettings))),
                .. InfoPlugins.Create(
                    new DictionaryLookup(Http), new WeatherService(Http), new QuoteService(Http), new FootballService(Http),
                    () => _settings.Current.WeatherCity, OpenUrl),
                new PluginEntry(BookmarksPlugin.Metadata, new BookmarksPlugin(
                    new BookmarkSource(BrowserProfiles.BookmarkFiles, _log).All, OpenUrl)),
                new PluginEntry(WindowsSettingsPlugin.Metadata, new WindowsSettingsPlugin(OpenUrl)),
                new PluginEntry(RecentFilesPlugin.Metadata, new RecentFilesPlugin(BrowserProfiles.RecentItems, OpenUrl)),
                new PluginEntry(WorldClockPlugin.Metadata, new WorldClockPlugin()),
                LinkEntry(LinkPlugin.GitHub(OpenUrl)),
                LinkEntry(LinkPlugin.LinkedIn(OpenUrl)),
                LinkEntry(LinkPlugin.LinkedIn(OpenUrl, misspelled: true)),
                new PluginEntry(ClipboardPlugin.Metadata, new ClipboardPlugin(_clipboard, PasteText)),
                new PluginEntry(FileSearchPlugin.Metadata, new FileSearchPlugin(_files, OpenPath, RevealPath)),
                new PluginEntry(DeepSearchPlugin.Metadata, new DeepSearchPlugin(
                    DeepSearchSettings, OpenPath, RevealPath)),
                new PluginEntry(NotesPlugin.Metadata, new NotesPlugin(notes)),
                new PluginEntry(EmojiPlugin.Metadata, new EmojiPlugin(PasteText)),
                new PluginEntry(WindowSwitcherPlugin.Metadata, new WindowSwitcherPlugin(
                    WindowEnumerator.List, ActivateWindow, WindowEnumerator.Close)),
                new PluginEntry(TimerPlugin.Metadata, new TimerPlugin(_timers)),
                .. SystemShortcuts(host).Select(p => new PluginEntry(p.Metadata, p)),
                new PluginEntry(KeywordHintsPlugin.Metadata, new KeywordHintsPlugin(() => plugins.Plugins.Select(p => p.Metadata))),
                new PluginEntry(CommandsPlugin.Metadata, new CommandsPlugin(commands)),
                new PluginEntry(CommandListPlugin.Metadata, new CommandListPlugin(() => ListCommands(commands, plugins, host))),
                new PluginEntry(VersionPlugin.Metadata, new VersionPlugin(() => new VersionInfo(
                    updates.CurrentVersion,
                    Changelog.GetNotes(releaseNotes, updates.CurrentVersion),
                    updates.Status))),
                .. ExternalPlugins.Load(ExtensionsRoot, _log),
            ],
            host,
            Path.Combine(DataRoot, "plugins"));

        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => _window.ToggleLauncher(Stopwatch.StartNew());
        var hotkey = _settings.Current.Hotkey;
        if (!RegisterHotkey(hotkey))
        {
            _log.Warn($"{hotkey} kaydedilemedi; başka bir uygulama kullanıyor olabilir.");
            _tray.ShowBalloon(
                "Kısayol alınamadı",
                $"{hotkey} başka bir uygulama tarafından kullanılıyor. Tepsi simgesine sağ tıklayıp Ayarlar'dan başka bir kısayol seçebilirsin.",
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
        new("LumoraGo ayarları", "Kısayol, tema, pano geçmişi ve arama klasörleri", _ =>
        {
            Dispatcher.BeginInvoke(OpenSettings);
            return true;
        }),
        new("LumoraGo'dan çık", "Launcher'ı tamamen kapatır", _ =>
        {
            Dispatcher.BeginInvoke(ExitApplication);
            return true;
        }),
        new("LumoraGo eklenti klasörünü aç", "Eklentiyi buraya kendi klasörüyle kopyala, sonra LumoraGo'yu yeniden başlat", _ =>
        {
            Directory.CreateDirectory(ExtensionsRoot);
            OpenPath(ExtensionsRoot);
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

    /// <summary>"/güncelle": beklemeden kontrol eder; yeni sürüm indiyse hemen kurup yeniden başlar.</summary>
    private async Task UpdateNowAsync(IPluginHost host)
    {
        if (_updates is null)
        {
            return;
        }

        host.ShowMessage("LumoraGo güncelleme", "Yeni sürüm kontrol ediliyor…");
        await _updates.CheckAndDownloadAsync();
        if (_updates.IsReadyToApply)
        {
            host.ShowMessage("LumoraGo güncelleme", _updates.Status.Replace("launcher kapanınca yüklenecek", "kuruluyor, LumoraGo yeniden açılacak"));
            _applyScheduled = true;
            _updateTimer?.Stop();
            _window?.HideLauncher();
            _updates.ApplyAndRestart();
            return;
        }

        host.ShowMessage("LumoraGo güncelleme", _updates.Status);
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

    private static PluginEntry RemindersEntry(RemindersPlugin plugin) => new(plugin.Metadata, plugin);

    /// <summary>Vakti gelen hatırlatıcıları tepside gösterir; LumoraGo kapalıyken kaçanlar açılıştan hemen sonra gelir.</summary>
    /// <returns>Ayarlar penceresinde gösterilecek sonuç.</returns>
    private async Task<string> SyncGoogleContactsAsync()
    {
        try
        {
            return await _googleContacts!.SyncAsync(CancellationToken.None) switch
            {
                null => "Google'a bağlı değil.",
                var n => $"{n} kişi alındı",
            };
        }
        catch (GoogleException ex)
        {
            _log?.Warn($"Google Rehber alınamadı: {ex.Message}");
            return ex.Message;
        }
    }

    private async Task<string> ConnectGoogleAsync(string clientId, string clientSecret)
    {
        try
        {
            var account = await GoogleContacts.SignInAsync(Http, clientId, clientSecret, OpenUrl, CancellationToken.None);
            _secrets!.Save(_secrets.Current with { Google = account });
        }
        catch (GoogleException ex)
        {
            return ex.Message;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            return $"Google'a bağlanılamadı ({ex.Message})";
        }

        return await SyncGoogleContactsAsync();
    }

    private void CheckReminders()
    {
        if (_reminders is null)
        {
            return;
        }

        var now = DateTime.Now;
        foreach (var reminder in _reminders.TakeDue(now))
        {
            var late = now - reminder.Due > TimeSpan.FromMinutes(2);
            Dispatcher.BeginInvoke(() =>
            {
                System.Media.SystemSounds.Exclamation.Play();
                _tray?.ShowBalloon($"🔔 {reminder.Text}", late
                    ? $"Kaçırılan hatırlatıcı ({reminder.Due:d MMM HH:mm}). /hatırlatıcılar ile tamamla ya da ertele."
                    : $"Saat {reminder.Due:HH:mm}. /hatırlatıcılar ile tamamla ya da ertele.");
            });
        }
    }

    private static PluginEntry ContactsEntry(ContactsPlugin plugin) => new(plugin.Metadata, plugin);

    private static PluginEntry PhotosEntry(PhotosPlugin plugin) => new(plugin.Metadata, plugin);

    private static PluginEntry TasksEntry(TasksPlugin plugin) => new(plugin.Metadata, plugin);

    private static PluginEntry DriveEntry(LiveLookupPlugin plugin) => new(plugin.Metadata, plugin);

    private static PluginEntry ScriptsEntry(ScriptsPlugin plugin) => new(plugin.Metadata, plugin);

    private (string Text, IReadOnlyList<ActionChain> Chains) _chainCache = ("", []);

    /// <summary>Ayarlardaki zincir metni; değişmedikçe yeniden ayrıştırılmaz.</summary>
    private IReadOnlyList<ActionChain> Chains()
    {
        var text = _settings?.Current.ScriptChains ?? "";
        var cache = _chainCache;
        if (cache.Text != text)
        {
            cache = (text, ChainText.Parse(text, out _));
            _chainCache = cache;
        }

        return cache.Chains;
    }

    private ChainActions ChainActionsFor(
        IPluginHost host, WindowsAppSource apps, IAppLauncher launcher, IMediaControl media, ScriptCatalog scripts, ScriptRunner runner) => new(
        Open: path =>
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return $"{path} bulunamadı";
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        },
        LaunchApp: name =>
        {
            if (ChainSteps.FindApp(apps.Apps, name) is not { } app)
            {
                return $"\"{name}\" adında uygulama bulunamadı";
            }

            launcher.Launch(app, LaunchMode.Normal);
            return null;
        },
        OpenUrl: url =>
        {
            Process.Start(new ProcessStartInfo(ChainSteps.NormalizeUrl(url)) { UseShellExecute = true });
            return null;
        },
        RunScript: (text, ct) => ChainSteps.RunScriptAsync(scripts, runner.RunAsync, text, ct),
        SetVolume: percent => media.SetVolume(percent) ? null : "ses düzeyi ayarlanamadı",
        Copy: text =>
        {
            host.CopyToClipboard(text);
            return null;
        },
        Notify: host.ShowMessage);

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
        new("/güncelle", "LumoraGo'yu şimdi güncelle", $"Yeni sürüm varsa hemen indirip kurar · şu an {_updates?.CurrentVersion}", () => _ = UpdateNowAsync(host)),
        new("/guncelle", "LumoraGo'yu şimdi güncelle", $"Yeni sürüm varsa hemen indirip kurar · şu an {_updates?.CurrentVersion}", () => _ = UpdateNowAsync(host)),
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

    private void OpenSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _window?.HideLauncher();
        _settingsWindow = new SettingsWindow(_settings, _secrets!, TryChangeHotkey, () => _learning?.Clear(), _contacts!,
            ConnectGoogleAsync, SyncGoogleContactsAsync);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(LauncherSettings settings)
    {
        _theme?.SetChoice(settings.Theme);
        if (_window is not null)
        {
            _window.PreviewAlwaysOpen = settings.PreviewAlwaysOpen;
        }

        if (settings.ClipboardHistory && _clipboardMonitor is null && _clipboard is not null)
        {
            _clipboardMonitor = new ClipboardMonitor(_clipboard, _log!);
        }
        else if (!settings.ClipboardHistory && _clipboardMonitor is not null)
        {
            _clipboardMonitor.Dispose();
            _clipboardMonitor = null;
            _clipboard?.Clear();
        }

        ApplyClipboardPersistence(settings);
        _contactsWatcher?.Watch(settings.ContactsFile, settings.ContactsWatch);

        // Klasörler değişmiş olabilir; dosya listesini arka planda yeniden kur.
        _ = Task.Run(() => _files?.Rebuild());
    }

    private static string ClipboardFile => Path.Combine(DataRoot, "clipboard.json");

    private void ApplyClipboardPersistence(LauncherSettings settings)
    {
        var persist = settings.ClipboardHistory && settings.ClipboardPersist;
        if (persist && _clipboardPersistence is null && _clipboard is not null)
        {
            _clipboardPersistence = new ClipboardPersistence(_clipboard, ClipboardFile, _log);
        }
        else if (!persist && _clipboardPersistence is not null)
        {
            _clipboardPersistence.Dispose();
            _clipboardPersistence = null;
            ClipboardPersistence.Delete(ClipboardFile, _log);
        }
    }

    private void TimerElapsed(RunningTimer timer)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var title = timer.Label.Length > 0 ? $"⏰ {timer.Label}" : "⏰ Süre doldu";
        _tray?.ShowBalloon(title, $"{TimerRequest.Format(timer.Duration)} doldu ({timer.EndsAt:HH:mm})");
    }

    /// <summary>Launcher gizlendikten sonra pencereyi öne getirir; yoksa gizlenen launcher odağı geri alabilir.</summary>
    private void ActivateWindow(Core.Windows.OpenWindow window) => Dispatcher.BeginInvoke(async () =>
    {
        _window?.HideLauncher();
        await Task.Delay(PasteDelay);
        WindowEnumerator.Activate(window);
    });

    /// <summary>"Ctrl+Shift+Space" gibi metinden kısayolu kaydeder.</summary>
    private bool RegisterHotkey(string text)
    {
        if (_hotkey is null
            || !HotkeyText.TryParse(text, out var modifiers, out var keyName)
            || !Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            return false;
        }

        var wpf = ModifierKeys.None;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) wpf |= ModifierKeys.Alt;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) wpf |= ModifierKeys.Control;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) wpf |= ModifierKeys.Shift;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) wpf |= ModifierKeys.Windows;
        return _hotkey.Register(wpf, key);
    }

    /// <summary>Yeni kısayolu dener; alınamazsa eskisini geri kaydeder.</summary>
    private bool TryChangeHotkey(string text)
    {
        if (RegisterHotkey(text))
        {
            return true;
        }

        RegisterHotkey(_settings?.Current.Hotkey ?? "Alt+Space");
        return false;
    }

    private IReadOnlyList<string> FileSearchFolders() =>
        UserFolders().Concat(_settings?.Current.FileSearchFolders ?? []).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private DeepSearchOptions DeepSearchSettings()
    {
        var settings = _settings?.Current ?? new LauncherSettings();
        var skip = DeepSearchOptions.BuildSkipSet(settings.DeepSearchIncludeSystem, settings.DeepSearchSkipNames);
        return new DeepSearchOptions
        {
            Roots = settings.DeepSearchFolders.Count > 0 ? settings.DeepSearchFolders : FixedDrives(),
            SkipDirectoryNames = skip,
        };
    }

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

    /// <summary>Metni panoya koyar, launcher'ı gizler ve önceki pencereye Ctrl+V gönderir.</summary>
    private void PasteText(string text) => Dispatcher.BeginInvoke(async () =>
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _log?.Warn($"Panoya yazılamadı: {ex.Message}");
            return;
        }

        _window?.HideLauncher();
        await Task.Delay(PasteDelay);
        SystemActions.SendPaste();
    });

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
        _clipboardMonitor?.Dispose();
        _contactsWatcher?.Dispose();
        _reminderTimer?.Dispose();
        _googleContactsTimer?.Dispose();
        _clipboardPersistence?.Dispose();
        _timers?.Dispose();
        _fileIndexTimer?.Dispose();
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
