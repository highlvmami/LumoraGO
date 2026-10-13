using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LumoraGo.App.Services;

/// <summary>"/kilit", "/uyku", "/cop" gibi Windows denetim kısayollarının işleri.</summary>
internal static class SystemActions
{
    public static void Lock() => LockWorkStation();

    public static void Sleep() =>
        System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);

    /// <summary>Bayrak verilmediği için Windows silmeden önce onay sorar.</summary>
    public static void EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0);

    /// <summary>Çalışan, geri döngü olmayan ve ağ geçidi olan ilk arabirimin IPv4 adresi; yoksa null.</summary>
    public static string? LocalIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Count > 0)
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?.ToString();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
