using System.IO;
using LumoraGo.App.Native;
using LumoraGo.Core.Windows;

namespace LumoraGo.App.Services;

/// <summary>Alt+Tab'da görünen pencereleri listeler, öne getirir ve kapatır.</summary>
internal static class WindowEnumerator
{
    public static IReadOnlyList<OpenWindow> List()
    {
        var ownProcess = (uint)Environment.ProcessId;
        var exeByProcess = new Dictionary<uint, string?>();
        var windows = new List<OpenWindow>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsAltTabWindow(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownProcess)
            {
                return true;
            }

            var title = GetTitle(hwnd);
            if (title.Length == 0)
            {
                return true;
            }

            if (!exeByProcess.TryGetValue(pid, out var exe))
            {
                exe = GetExePath(pid);
                exeByProcess[pid] = exe;
            }

            var name = exe is null ? "" : Path.GetFileNameWithoutExtension(exe);
            windows.Add(new OpenWindow(hwnd, title, name, exe));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static void Activate(OpenWindow window)
    {
        if (NativeMethods.IsIconic(window.Handle))
        {
            NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);
        }

        ForegroundHelper.ForceForeground(window.Handle);
    }

    public static void Close(OpenWindow window) =>
        NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    private static bool IsAltTabWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)) != 0)
        {
            return false;
        }

        // Başka masaüstündeki veya askıdaki UWP pencereleri görünür sayılır ama "cloaked" olur.
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, copied).Trim();
    }

    private static string? GetExePath(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
