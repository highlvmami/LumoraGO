using System.Windows.Interop;
using System.Windows.Media;
using LumoraGo.App.Native;

namespace LumoraGo.App.Services;

/// <summary>Windows 11 pencere efektleri: akrilik arka plan, yuvarlak köşe, koyu başlık modu. Windows 10'da sessizce atlanır.</summary>
internal static class WindowEffects
{
    private static int Build => Environment.OSVersion.Version.Build;

    /// <summary>Sistem arka planı (akrilik) Windows 11 22H2 (22621) ile geldi.</summary>
    public static bool SupportsBackdrop => OperatingSystem.IsWindows() && Build >= 22621;

    /// <summary>Yuvarlak köşe tercihi Windows 11 (22000) ile geldi.</summary>
    public static bool SupportsRoundedCorners => OperatingSystem.IsWindows() && Build >= 22000;

    public static void Apply(IntPtr hwnd, bool dark)
    {
        SetDarkMode(hwnd, dark);

        if (SupportsRoundedCorners)
        {
            var corner = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        if (SupportsBackdrop)
        {
            // WPF'in kendi arka planı DWM'in çizdiği akriliği örtmesin.
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            var backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
    }

    /// <summary>Akriliğin koyu veya açık tonda çizilmesini belirler.</summary>
    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
