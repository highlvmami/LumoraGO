using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumoraGo.App.Native;
using LumoraGo.Core.Plugins;

namespace LumoraGo.App.Services;

/// <summary>
/// Uygulama simgeleri. Kısayol yolu veya "shell:AppsFolder\AUMID" verilir, Windows'un gösterdiği simge (ok işaretsiz)
/// arka planda çıkarılıp bellekte tutulur. UI sadece hazır olanı okur.
/// </summary>
internal static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static StaWorker? _worker;

    public static void Initialize(StaWorker worker) => _worker = worker;

    /// <summary>Bir küçük resim (<see cref="PhotosPlugin.ThumbnailPrefix"/>) hazır olunca, işçi iş parçacığında çağrılır.</summary>
    public static event Action? ThumbnailLoaded;

    /// <returns>Simge hazırsa simge; değilse null (ve arka planda yüklemeye başlar).</returns>
    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (Icons.TryGetValue(path, out var icon))
        {
            return icon;
        }

        Warm([path]);
        return null;
    }

    public static void Warm(IEnumerable<string> paths)
    {
        if (_worker is null)
        {
            return;
        }

        var pending = paths.Where(p => !Icons.ContainsKey(p)).ToList();
        foreach (var path in pending)
        {
            // Aynı yolu iki kez kuyruğa koymamak için yer tutucu
            Icons.TryAdd(path, null);
        }

        if (pending.Count > 0)
        {
            _worker.Post(() =>
            {
                var thumbnails = false;
                foreach (var path in pending)
                {
                    if (path.StartsWith(PhotosPlugin.ThumbnailPrefix, StringComparison.Ordinal))
                    {
                        Icons[path] = ShellPreview.Thumbnail(path[PhotosPlugin.ThumbnailPrefix.Length..], 64);
                        thumbnails = true;
                    }
                    else
                    {
                        Icons[path] = Load(path);
                    }
                }

                if (thumbnails)
                {
                    ThumbnailLoaded?.Invoke();
                }
            });
        }
    }

    private static ImageSource? Load(string path)
    {
        if (NativeMethods.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo(
                pidl, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_PIDL | NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }
}
