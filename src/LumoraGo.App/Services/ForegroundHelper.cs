using LumoraGo.App.Native;

namespace LumoraGo.App.Services;

/// <summary>
/// Windows, arka plandaki bir işlemin kendini öne almasını kısıtlar (SetForegroundWindow kuralları).
/// Kısayol mesajı genelde bu izni verir; vermezse öndeki pencerenin girdi kuyruğuna bağlanıp tekrar denenir.
/// </summary>
internal static class ForegroundHelper
{
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (NativeMethods.SetForegroundWindow(hwnd) && NativeMethods.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attached = foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            // Sahte bir Alt basıp bırakmak "son girdi bu işlemden geldi" sayılmasını sağlar.
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }
}
