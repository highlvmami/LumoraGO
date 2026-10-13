using System.Text.Json;

namespace LumoraGo.Core.Clipboard;

/// <summary>
/// Pano geçmişini diske yazar ve açılışta geri yükler. Değişiklikler kısa bir gecikmeyle toplu kaydedilir;
/// <see cref="Dispose"/> bekleyen kaydı hemen yapar.
/// </summary>
public sealed class ClipboardPersistence : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ClipboardHistory _history;
    private readonly string _path;
    private readonly ILog _log;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _pending;
    private bool _disposed;

    public ClipboardPersistence(ClipboardHistory history, string path, ILog? log = null, TimeSpan? saveDelay = null)
    {
        _history = history;
        _path = path;
        _log = log ?? NullLog.Instance;
        _delay = saveDelay ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Flush());

        history.AddOlder(Load());
        history.Changed += OnChanged;
    }

    /// <summary>Kalıcı geçmiş kapatılınca dosyayı siler.</summary>
    public static void Delete(string path, ILog? log = null)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (log ?? NullLog.Instance).Warn($"Pano geçmişi dosyası silinemedi: {ex.Message}");
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_history.Entries, Json));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Pano geçmişi kaydedilemedi: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _history.Changed -= OnChanged;
        _timer.Dispose();
        Flush();
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private List<ClipboardEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ClipboardEntry>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"Pano geçmişi okunamadı, boş başlıyor: {ex.Message}");
            return [];
        }
    }
}
