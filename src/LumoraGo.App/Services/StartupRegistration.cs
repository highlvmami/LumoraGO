using Microsoft.Win32;

namespace LumoraGo.App.Services;

/// <summary>Windows açılışında başlatma: HKCU\...\Run anahtarı. Yönetici izni gerekmez.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LumoraGo";
    public const string BackgroundArg = "--background";

    private static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArg}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
