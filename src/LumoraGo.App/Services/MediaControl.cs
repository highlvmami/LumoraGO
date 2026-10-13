using System.Runtime.InteropServices;
using LumoraGo.App.Native;
using LumoraGo.Core;
using LumoraGo.Core.Desktop;

namespace LumoraGo.App.Services;

/// <summary>Medya tuşlarını klavye olayı olarak gönderir; ses düzeyini Windows Core Audio ile okur ve ayarlar.</summary>
internal sealed class MediaControl(ILog log) : IMediaControl
{
    public int? GetVolume()
    {
        try
        {
            var volume = AudioEndpoint();
            try
            {
                Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
                return (int)Math.Round(level * 100);
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            log.Warn($"Ses düzeyi okunamadı: {ex.Message}");
            return null;
        }
    }

    public bool SetVolume(int percent)
    {
        try
        {
            var volume = AudioEndpoint();
            try
            {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
                Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context));
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            log.Warn($"Ses düzeyi ayarlanamadı: {ex.Message}");
            return false;
        }
    }

    public void Send(MediaKey key)
    {
        var vk = key switch
        {
            MediaKey.PlayPause => NativeMethods.VK_MEDIA_PLAY_PAUSE,
            MediaKey.Next => NativeMethods.VK_MEDIA_NEXT_TRACK,
            MediaKey.Previous => NativeMethods.VK_MEDIA_PREV_TRACK,
            MediaKey.Mute => NativeMethods.VK_VOLUME_MUTE,
            MediaKey.VolumeUp => NativeMethods.VK_VOLUME_UP,
            _ => NativeMethods.VK_VOLUME_DOWN,
        };
        NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static IAudioEndpointVolume AudioEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device));
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var endpoint));
                return (IAudioEndpointVolume)endpoint;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    }
}
