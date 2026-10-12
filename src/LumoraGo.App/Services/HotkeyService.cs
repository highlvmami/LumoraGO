using System.Windows.Input;
using System.Windows.Interop;
using LumoraGo.App.Native;

namespace LumoraGo.App.Services;

/// <summary>
/// RegisterHotKey ile sistem genelinde kısayol. Mesajları almak için görünmez, yalnızca mesaj alan bir pencere kullanır.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4C47; // "LG"
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("LumoraGoHotkeySink")
        {
            ParentWindow = HwndMessage,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public string? Description { get; private set; }

    /// <returns>Kısayol başka bir uygulamada kayıtlıysa false.</returns>
    public bool Register(ModifierKeys modifiers, Key key)
    {
        Unregister();

        var mods = NativeMethods.HotkeyModifiers.NoRepeat;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.HotkeyModifiers.Win;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, mods, vk);
        Description = $"{modifiers}+{key}".Replace(", ", "+");
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
