using System.Windows;
using System.Windows.Input;
using LumoraGo.App.Services;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Scripts;
using LumoraGo.Core.Settings;

namespace LumoraGo.App;

/// <summary>Kısayol, tema, açılışta başlatma, pano geçmişi ve arama klasörleri.</summary>
internal partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly SecretStore _secrets;
    private readonly Func<string, bool> _tryHotkey;
    private readonly Action _resetLearning;
    private readonly ContactBook _contacts;
    private readonly Func<string, string, Task<string>> _connectGoogle;
    private readonly Func<Task<string>> _syncGoogle;
    private string _contactsFile;

    /// <param name="tryHotkey">Kısayolu kaydetmeyi dener; başka uygulama kullanıyorsa false döner ve eskisi kalır.</param>
    public SettingsWindow(SettingsStore store, SecretStore secrets, Func<string, bool> tryHotkey, Action resetLearning, ContactBook contacts,
        Func<string, string, Task<string>> connectGoogle, Func<Task<string>> syncGoogle)
    {
        InitializeComponent();

        // Launcher'ın yarı saydam arka planı akriliksiz bir pencerede siyah görünür; temanın opak rengini kullan.
        Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("WindowBackgroundOpaqueColor"));
        _store = store;
        _secrets = secrets;
        _tryHotkey = tryHotkey;
        _resetLearning = resetLearning;
        _contacts = contacts;
        _connectGoogle = connectGoogle;
        _syncGoogle = syncGoogle;

        var s = store.Current;
        HotkeyBox.Text = s.Hotkey;
        ThemeBox.SelectedIndex = (int)s.Theme;
        StartupBox.IsChecked = StartupRegistration.IsEnabled;
        PreviewAlwaysOpenBox.IsChecked = s.PreviewAlwaysOpen;
        WeatherCityBox.Text = s.WeatherCity;
        ClipboardBox.IsChecked = s.ClipboardHistory;
        ClipboardPersistBox.IsChecked = s.ClipboardPersist;
        Fill(DeepFoldersList, s.DeepSearchFolders);
        Fill(FileFoldersList, s.FileSearchFolders);
        DeepIncludeSystemBox.IsChecked = s.DeepSearchIncludeSystem;
        SkipNamesBox.Text = string.Join(", ", s.DeepSearchSkipNames);
        CustomShortcutsBox.Text = CustomShortcutText.Format(s.CustomShortcuts);
        ScriptChainsBox.Text = s.ScriptChains;
        _contactsFile = s.ContactsFile;
        ContactsWatchBox.IsChecked = s.ContactsWatch;
        ContactsGlobalBox.IsChecked = s.ContactsInGlobalSearch;
        ShowContactsStatus();
        if (secrets.Current.Google is { } google)
        {
            GoogleClientIdBox.Text = google.ClientId;
            GoogleClientSecretBox.Password = google.ClientSecret;
        }

        ShowGoogleStatus();
        CalendarUrlsBox.Text = string.Join(Environment.NewLine, secrets.Current.CalendarUrls);
        if (secrets.Current.Mail is { } mail)
        {
            MailAddressBox.Text = mail.Address;
            MailServerBox.Text = AccountInput.ServerText(mail);
            MailPasswordNote.Text = "Şifre kayıtlı. Değiştirmek istemiyorsan kutuyu boş bırak.";
        }

        DefaultSkipNote.Text = "Virgülle ayır. Bunlar her zaman atlanır: "
            + string.Join(", ", DeepSearchOptions.DefaultSkipDirectoryNames.Order(StringComparer.OrdinalIgnoreCase));
    }

    private static void Fill(System.Windows.Controls.ListBox list, IEnumerable<string> items)
    {
        list.Items.Clear();
        foreach (var item in items)
        {
            list.Items.Add(item);
        }
    }

    private static List<string> Items(System.Windows.Controls.ListBox list) => list.Items.Cast<string>().ToList();

    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Win;

        if (modifiers != HotkeyModifiers.None)
        {
            HotkeyBox.Text = HotkeyText.Format(modifiers, key.ToString());
        }
    }

    private void ResetLearning_OnClick(object sender, RoutedEventArgs e)
    {
        _resetLearning();
        ResetLearningNote.Text = "Öğrenilen sıralama sıfırlandı.";
    }

    private void ShowContactsStatus() =>
        ContactsStatus.Text = _contacts.All.Count == 0
            ? "Rehber boş."
            : $"{_contacts.All.Count} kişi" + (_contactsFile.Length > 0 ? $" · {System.IO.Path.GetFileName(_contactsFile)}" : "");

    private void ImportContacts_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kişi dosyası seç",
            Filter = "Kişi dosyaları (*.vcf;*.csv)|*.vcf;*.csv|Tüm dosyalar|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = _contacts.Import(dialog.FileName);
            _contactsFile = dialog.FileName;
            ShowContactsStatus();
            ContactsStatus.Text += $" (az önce {count} kişi aktarıldı)";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Kişiler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowGoogleStatus()
    {
        var google = _secrets.Current.Google;
        var count = _contacts.All.Count(c => c.FromGoogle);
        GoogleStatus.Text = google is { RefreshToken.Length: > 0 }
            ? $"Bağlı{(google.Email.Length > 0 ? ": " + google.Email : "")} · {count} kişi · 3 saatte bir yenilenir"
            : "Bağlı değil.";
        GoogleSyncButton.IsEnabled = GoogleDisconnectButton.IsEnabled = google is { RefreshToken.Length: > 0 };
    }

    private async void ConnectGoogle_OnClick(object sender, RoutedEventArgs e)
    {
        var id = GoogleClientIdBox.Text.Trim();
        var secret = GoogleClientSecretBox.Password.Trim();
        if (id.Length == 0 || secret.Length == 0)
        {
            MessageBox.Show(this, "Önce Google Cloud'da oluşturduğun masaüstü istemcisinin kimliğini ve gizli anahtarını yapıştır (adımlar kutuların altında).",
                "Google Rehber", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunGoogle(GoogleConnectButton, "Tarayıcıda Google hesabını seç ve izin ver…", () => _connectGoogle(id, secret));
    }

    private async void SyncGoogle_OnClick(object sender, RoutedEventArgs e) =>
        await RunGoogle(GoogleSyncButton, "Google Rehber okunuyor…", _syncGoogle);

    private async Task RunGoogle(System.Windows.Controls.Button button, string working, Func<Task<string>> action)
    {
        button.IsEnabled = false;
        GoogleStatus.Text = working;
        try
        {
            var message = await action();
            ShowGoogleStatus();
            ShowContactsStatus();
            GoogleStatus.Text += $" ({message})";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void DisconnectGoogle_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Google bağlantısı kesilsin ve Google'dan gelen kişiler LumoraGo'dan silinsin mi? Google Rehber'in etkilenmez.",
                "Google Rehber", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _secrets.Save(_secrets.Current with { Google = null });
        _contacts.RemoveGoogle();
        GoogleClientSecretBox.Password = "";
        ShowGoogleStatus();
        ShowContactsStatus();
    }

    private void ClearContacts_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "İçe aktarılan tüm kişiler LumoraGo'dan silinsin mi? Asıl dosyan etkilenmez.", "Kişiler",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _contacts.Clear();
        _contactsFile = "";
        ContactsWatchBox.IsChecked = false;
        ShowContactsStatus();
    }

    private void AddDeepFolder_OnClick(object sender, RoutedEventArgs e) => AddFolder(DeepFoldersList);

    private void RemoveDeepFolder_OnClick(object sender, RoutedEventArgs e) => RemoveSelected(DeepFoldersList);

    private void AddFileFolder_OnClick(object sender, RoutedEventArgs e) => AddFolder(FileFoldersList);

    private void RemoveFileFolder_OnClick(object sender, RoutedEventArgs e) => RemoveSelected(FileFoldersList);

    private void AddFolder(System.Windows.Controls.ListBox list)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Klasör seç", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var folder in dialog.FolderNames)
        {
            if (!list.Items.Cast<string>().Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                list.Items.Add(folder);
            }
        }
    }

    private static void RemoveSelected(System.Windows.Controls.ListBox list)
    {
        if (list.SelectedItem is { } item)
        {
            list.Items.Remove(item);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var current = _store.Current;
        var shortcuts = CustomShortcutText.Parse(CustomShortcutsBox.Text, out var shortcutErrors);
        if (shortcutErrors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, shortcutErrors), "Özel kısayollar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ChainText.Parse(ScriptChainsBox.Text, out var chainErrors);
        if (chainErrors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, chainErrors), "Eylem zincirleri", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var calendarUrls = AccountInput.ParseCalendarUrls(CalendarUrlsBox.Text, out var calendarErrors);
        if (calendarErrors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, calendarErrors), "Takvim (/takvim)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (mail, mailError) = AccountInput.BuildMail(MailAddressBox.Text, MailPasswordBox.Password, MailServerBox.Text, _secrets.Current.Mail);
        if (mailError is not null)
        {
            MessageBox.Show(this, mailError, "Mail (/mail)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HotkeyBox.Text != current.Hotkey && !_tryHotkey(HotkeyBox.Text))
        {
            MessageBox.Show(this, $"{HotkeyBox.Text} başka bir uygulama tarafından kullanılıyor. Başka bir birleşim dene.",
                "Kısayol alınamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (StartupBox.IsChecked != StartupRegistration.IsEnabled)
        {
            StartupRegistration.SetEnabled(StartupBox.IsChecked == true);
        }

        if (!calendarUrls.SequenceEqual(_secrets.Current.CalendarUrls) || mail != _secrets.Current.Mail)
        {
            _secrets.Save(_secrets.Current with { CalendarUrls = calendarUrls, Mail = mail });
        }

        _store.Save(current with
        {
            Hotkey = HotkeyBox.Text,
            Theme = (ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex),
            PreviewAlwaysOpen = PreviewAlwaysOpenBox.IsChecked == true,
            WeatherCity = WeatherCityBox.Text.Trim(),
            ClipboardHistory = ClipboardBox.IsChecked == true,
            ClipboardPersist = ClipboardBox.IsChecked == true && ClipboardPersistBox.IsChecked == true,
            DeepSearchFolders = Items(DeepFoldersList),
            DeepSearchIncludeSystem = DeepIncludeSystemBox.IsChecked == true,
            DeepSearchSkipNames = SkipNamesBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FileSearchFolders = Items(FileFoldersList),
            CustomShortcuts = shortcuts,
            ScriptChains = ScriptChainsBox.Text.Trim(),
            ContactsFile = _contactsFile,
            ContactsWatch = ContactsWatchBox.IsChecked == true && _contactsFile.Length > 0,
            ContactsInGlobalSearch = ContactsGlobalBox.IsChecked == true,
        });
        Close();
    }
}
