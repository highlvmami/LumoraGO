namespace LumoraGo.Core.Desktop;

/// <summary>Çalışan bir işlem.</summary>
/// <param name="MemoryBytes">Çalışma kümesi (bellekte kapladığı yer).</param>
public sealed record RunningProcess(int Id, string Name, long MemoryBytes, string? ExePath);

public enum MediaKey
{
    PlayPause,
    Next,
    Previous,
    Mute,
    VolumeUp,
    VolumeDown,
}

/// <summary>Ses ve medya tuşları; uygulama tarafında Windows API'siyle uygulanır.</summary>
public interface IMediaControl
{
    /// <returns>0-100 arası ses düzeyi; okunamıyorsa null.</returns>
    int? GetVolume();

    bool SetVolume(int percent);

    void Send(MediaKey key);
}
