using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LumoraGo.App.Native;

/// <summary>Gezgin'in önizleme bölmesindeki önizleyiciler (IPreviewHandler), kısayol çözümü ve büyük küçük resimler.</summary>
internal static class ShellPreview
{
    private const string PreviewHandlerKey = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
    }

    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref RECT rect);

        void SetRect(ref RECT rect);

        void DoPreview();

        void Unload();

        void SetFocus();

        void QueryFocus(out IntPtr phwnd);

        [PreserveSig]
        uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Cx, Cy;
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkObject
    {
    }

    private const uint StgmRead = 0;
    private const uint ClsctxInprocServer = 0x1;
    private const uint ClsctxLocalServer = 0x4;
    private static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string pszFile, uint grfMode, uint dwAttributes, bool fCreate, IStream? pstmTemplate, out IStream ppstm);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Uzantı için kayıtlı önizleyicinin CLSID'si; yoksa null.</summary>
    public static Guid? FindHandler(string extension)
    {
        foreach (var keyPath in Candidates(extension))
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"{keyPath}\shellex\{PreviewHandlerKey}");
            if (key?.GetValue(null) is string value && Guid.TryParse(value, out var clsid))
            {
                return clsid;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string extension)
    {
        yield return extension;
        using (var ext = Registry.ClassesRoot.OpenSubKey(extension))
        {
            if (ext?.GetValue(null) is string progId && progId.Length > 0)
            {
                yield return progId;
            }
        }

        yield return $@"SystemFileAssociations\{extension}";
    }

    /// <summary>Önizleyiciyi oluşturup dosyayla başlatır. Gezgin gibi önce ayrı işlemde (prevhost) dener.</summary>
    /// <param name="stream">Akışla başlatıldıysa önizleyici kapanana kadar canlı tutulmalı.</param>
    public static IPreviewHandler? Create(Guid clsid, string path, out object? stream)
    {
        stream = null;
        var iid = IUnknownId;
        object? instance = null;
        foreach (var context in new[] { ClsctxLocalServer, ClsctxInprocServer })
        {
            if (CoCreateInstance(ref clsid, IntPtr.Zero, context, ref iid, out var created) == 0)
            {
                instance = created;
                break;
            }
        }

        if (instance is not IPreviewHandler handler)
        {
            Release(instance);
            return null;
        }

        try
        {
            if (instance is IInitializeWithFile withFile)
            {
                withFile.Initialize(path, StgmRead);
                return handler;
            }

            if (instance is IInitializeWithStream withStream
                && SHCreateStreamOnFileEx(path, StgmRead, 0, false, null, out var fileStream) == 0)
            {
                withStream.Initialize(fileStream, StgmRead);
                stream = fileStream;
                return handler;
            }
        }
        catch (COMException)
        {
            // Aşağıda bırakılır.
        }

        Release(instance);
        return null;
    }

    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    /// <summary>.lnk kısayolunun gösterdiği yol; çözülemezse kısayolun kendisi.</summary>
    public static string ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        object? link = null;
        try
        {
            link = new ShellLinkObject();
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, 0);
            return target.Length > 0 ? target.ToString() : path;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or System.IO.FileNotFoundException)
        {
            return path;
        }
        finally
        {
            Release(link);
        }
    }

    /// <summary>Windows'un küçük resmi (video karesi, albüm kapağı, simge). Bulunamazsa null.</summary>
    public static BitmapSource? Thumbnail(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0)
        {
            return null;
        }

        try
        {
            // SIIGBF_RESIZETOFIT = 0
            if (factory.GetImage(new SIZE { Cx = size, Cy = size }, 0, out var bitmap) != 0 || bitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            Release(factory);
        }
    }
}
