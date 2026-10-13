namespace LumoraGo.Core.Windows;

/// <summary>Görev çubuğunda görünen açık bir pencere.</summary>
/// <param name="Handle">Pencere tutamacı (HWND).</param>
/// <param name="ExePath">Simgesi gösterilecek uygulama; bilinmiyorsa null.</param>
public sealed record OpenWindow(IntPtr Handle, string Title, string ProcessName, string? ExePath);
