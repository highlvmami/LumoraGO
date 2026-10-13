using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LumoraGo.App.Services;

/// <summary>Windows'un açık/koyu uygulama temasını izler ve renk sözlüğünü değiştirir.</summary>
internal sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Application _app;
    private readonly bool _translucent;
    private ResourceDictionary? _current;

    private readonly bool _followSystem;

    /// <param name="translucent">Akrilik arka plan destekleniyorsa yarı saydam renkler kullanılır.</param>
    /// <param name="followSystem">false ise Windows ayarı ne olursa olsun koyu tema kullanılır.</param>
    public ThemeService(Application app, bool translucent, bool followSystem)
    {
        _app = app;
        _translucent = translucent;
        _followSystem = followSystem;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? Changed;

    public bool IsDark { get; private set; }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            _app.Dispatcher.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        var dark = !_followSystem || ReadIsDark();
        if (_current is not null && dark == IsDark)
        {
            return;
        }

        IsDark = dark;
        var theme = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/LumoraGo;component/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };

        if (!_translucent)
        {
            theme["WindowBackground"] = new SolidColorBrush((Color)theme["WindowBackgroundOpaqueColor"]);
        }

        var dictionaries = _app.Resources.MergedDictionaries;
        if (_current is not null)
        {
            dictionaries.Remove(_current);
        }

        dictionaries.Add(theme);
        _current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
