using System.Runtime.InteropServices;
using LumoraGo.App.Native;
using LumoraGo.Core.Plugins;
using Forms = System.Windows.Forms;

namespace LumoraGo.App.Services;

/// <summary>
/// Launcher açılmadan önce öndeki pencereyi taşır/boyutlandırır. Windows 10/11 pencerelerinin görünmez kenarlıkları
/// (DWM gölge payı) hesaba katılır, yoksa pencereler arasında boşluk kalır.
/// </summary>
internal static class WindowLayout
{
    private const int SW_MAXIMIZE = 3;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    /// <returns>Hata varsa kullanıcıya gösterilecek mesaj, yoksa null.</returns>
    public static string? Apply(IntPtr hwnd, WindowPlacement placement)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return "Yerleştirilecek pencere bulunamadı. Önce bir pencereye tıklayıp sonra launcher'ı aç.";
        }

        if (placement == WindowPlacement.Minimize)
        {
            NativeMethods.ShowWindow(hwnd, SW_MINIMIZE);
            return null;
        }

        var screen = Forms.Screen.FromHandle(hwnd);
        if (placement == WindowPlacement.Maximize)
        {
            NativeMethods.ShowWindow(hwnd, SW_MAXIMIZE);
            NativeMethods.SetForegroundWindow(hwnd);
            return null;
        }

        if (NativeMethods.IsIconic(hwnd) || IsZoomed(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, SW_RESTORE);
        }

        var visible = VisibleBounds(hwnd);
        var current = new ScreenRect(visible.Left, visible.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
        ScreenRect target;
        if (placement == WindowPlacement.NextScreen)
        {
            var screens = Forms.Screen.AllScreens;
            if (screens.Length < 2)
            {
                return "Tek ekran bağlı; taşınacak başka ekran yok.";
            }

            var next = screens[(Array.IndexOf(screens, screens.First(s => s.DeviceName == screen.DeviceName)) + 1) % screens.Length];
            target = WindowLayoutPlugin.MoveTo(current, ToRect(screen.WorkingArea), ToRect(next.WorkingArea));
        }
        else
        {
            target = WindowLayoutPlugin.Target(placement, ToRect(screen.WorkingArea), current);
        }

        // GetWindowRect görünmez kenarlıkları da içerir; farkı ekleyip görünen kısmı hedefe oturt.
        GetWindowRect(hwnd, out var outer);
        var (dl, dt) = (visible.Left - outer.Left, visible.Top - outer.Top);
        var (dr, db) = (outer.Right - visible.Right, outer.Bottom - visible.Bottom);
        SetWindowPos(hwnd, IntPtr.Zero, target.Left - dl, target.Top - dt, target.Width + dl + dr, target.Height + dt + db,
            SWP_NOZORDER | SWP_NOACTIVATE);
        NativeMethods.SetForegroundWindow(hwnd);
        return null;
    }

    private static RECT VisibleBounds(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0 ? r
        : GetWindowRect(hwnd, out var w) ? w : default;

    private static ScreenRect ToRect(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Width, r.Height);
}
