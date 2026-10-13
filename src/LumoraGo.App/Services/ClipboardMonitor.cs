using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LumoraGo.Core;
using LumoraGo.Core.Clipboard;

namespace LumoraGo.App.Services;

/// <summary>
/// Panoya kopyalanan metinleri <see cref="ClipboardHistory"/>'ye ekler. Şifre yöneticilerinin "izleme dışı" işaretlediği
/// içerikler kaydedilmez.
/// </summary>
internal sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    // Windows'un ve şifre yöneticilerinin "bunu geçmişe alma" işaretleri.
    private static readonly string[] ExcludeFormats =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
    ];

    private readonly ClipboardHistory _history;
    private readonly ILog _log;
    private readonly HwndSource _source;

    public ClipboardMonitor(ClipboardHistory history, ILog log)
    {
        _history = history;
        _log = log;
        _source = new HwndSource(new HwndSourceParameters("LumoraGo.Clipboard") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
        if (!AddClipboardFormatListener(_source.Handle))
        {
            _log.Warn($"Pano izlenemiyor: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            Capture();
        }

        return IntPtr.Zero;
    }

    private void Capture()
    {
        // Pano kopyalayan uygulamada kısa süre kilitli olabilir; birkaç kez dene.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (ExcludeFormats.Any(System.Windows.Clipboard.ContainsData) || !System.Windows.Clipboard.ContainsText())
                {
                    return;
                }

                _history.Add(System.Windows.Clipboard.GetText());
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
        }

        _log.Warn("Pano okunamadı.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
