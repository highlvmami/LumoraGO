using System.Windows;
using System.Windows.Input;
using LumoraGo.App.Services;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Settings;

namespace LumoraGo.App;

/// <summary>Kısayol, tema, açılışta başlatma, pano geçmişi ve arama klasörleri.</summary>
internal partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly SecretStore _secrets;
    private readonly Func<string, bool> _tryHotkey;
    private readonly Action _resetLearning;

    /// <param name="tryHotkey">Kısayolu kaydetmeyi dener; başka uygulama kullanıyorsa false döner ve eskisi kalır.</param>
    public SettingsWindow(SettingsStore store, SecretStore secrets, Func<string, bool> tryHotkey, Action resetLearning)
    {
        InitializeComponent();

        // Launcher'ın yarı saydam arka planı akriliksiz bir pencerede siyah görünür; temanın opak rengini kullan.
        Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("WindowBackgroundOpaqueColor"));
        _store = store;
        _secrets = secrets;
        _tryHotkey = tryHotkey;
        _resetLearning = resetLearning;

        var s = store.Current;
        HotkeyBox.Text = s.Hotkey;
        ThemeBox.SelectedIndex = (int)s.Theme;
        StartupBox.IsChecked = StartupRegistration.IsEnabled;
        PreviewAlwaysOpenBox.IsChecked = s.PreviewAlwaysOpen;
        ClipboardBox.IsChecked = s.ClipboardHistory;
        ClipboardPersistBox.IsChecked = s.ClipboardPersist;
        Fill(DeepFoldersList, s.DeepSearchFolders);
        Fill(FileFoldersList, s.FileSearchFolders);
        DeepIncludeSystemBox.IsChecked = s.DeepSearchIncludeSystem;
        SkipNamesBox.Text = string.Join(", ", s.DeepSearchSkipNames);
        CustomShortcutsBox.Text = CustomShortcutText.Format(s.CustomShortcuts);
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
            _secrets.Save(new AccountSecrets { CalendarUrls = calendarUrls, Mail = mail });
        }

        _store.Save(current with
        {
            Hotkey = HotkeyBox.Text,
            Theme = (ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex),
            PreviewAlwaysOpen = PreviewAlwaysOpenBox.IsChecked == true,
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
        });
        Close();
    }
}
