using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LumoraGo.App.Native;

/// <summary>
/// Gezgin önizleyicisini (PDF, Word, Excel…) WPF içinde bir alt pencerede gösterir. Önizleyici açılamazsa
/// <see cref="Failed"/> tetiklenir ve panel bilgi görünümüne döner.
/// </summary>
internal sealed class PreviewHandlerHost(Guid clsid, string path) : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;

    private IntPtr _hwnd;
    private ShellPreview.IPreviewHandler? _handler;
    private object? _stream;

    public event EventHandler? Failed;

    /// <summary>Önizleyici odak alırsa launcher kapanmasın diye odağı geri vermek için.</summary>
    public event EventHandler? PreviewShown;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "static", null, WsChild | WsVisible | WsClipChildren, 0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, Load);
        return new HandleRef(this, _hwnd);
    }

    private void Load()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _handler = ShellPreview.Create(clsid, path, out _stream);
            if (_handler is null)
            {
                Failed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var rect = Bounds();
            _handler.SetWindow(_hwnd, ref rect);
            _handler.SetRect(ref rect);
            _handler.DoPreview();
            PreviewShown?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException)
        {
            Unload();
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private ShellPreview.RECT Bounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new ShellPreview.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Max(1, ActualWidth * dpi.DpiScaleX),
            Bottom = (int)Math.Max(1, ActualHeight * dpi.DpiScaleY),
        };
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_handler is not null)
        {
            try
            {
                var rect = Bounds();
                _handler.SetRect(ref rect);
            }
            catch (COMException)
            {
                // Önizleyici kapanmış olabilir.
            }
        }
    }

    private void Unload()
    {
        if (_handler is not null)
        {
            try
            {
                _handler.Unload();
            }
            catch (COMException)
            {
                // Zaten kapanmış.
            }

            ShellPreview.Release(_handler);
            _handler = null;
        }

        ShellPreview.Release(_stream);
        _stream = null;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Unload();
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }
}
