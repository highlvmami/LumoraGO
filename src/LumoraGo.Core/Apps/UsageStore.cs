using System.Text.Json;

namespace LumoraGo.Core.Apps;

/// <summary>
/// Hangi uygulamanın ne sıklıkla ve en son ne zaman açıldığını tutar; sık ve yeni kullanılan üste çıkar.
/// Diske JSON olarak yazılır.
/// </summary>
public sealed class UsageStore
{
    private readonly string? _path;
    private readonly ILog _log;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private Dictionary<string, UsageRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public UsageStore(string? path, ILog? log = null, Func<DateTime>? now = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        _now = now ?? (() => DateTime.UtcNow);
        Load();
    }

    public sealed record UsageRecord(int Count, DateTime LastUsedUtc);

    public void Record(string id)
    {
        Dictionary<string, UsageRecord> snapshot;
        lock (_gate)
        {
            var count = _records.TryGetValue(id, out var existing) ? existing.Count + 1 : 1;
            _records[id] = new UsageRecord(count, _now());
            snapshot = new Dictionary<string, UsageRecord>(_records, StringComparer.OrdinalIgnoreCase);
        }

        _ = Task.Run(() => Save(snapshot));
    }

    /// <summary>Sıralamaya eklenecek puan: kullanım sayısı (üst sınırlı) + yakın zamanda kullanıldıysa ek puan.</summary>
    public int Boost(string id)
    {
        UsageRecord? record;
        lock (_gate)
        {
            _records.TryGetValue(id, out record);
        }

        if (record is null)
        {
            return 0;
        }

        var age = _now() - record.LastUsedUtc;
        var recency = age < TimeSpan.FromHours(1) ? 30
            : age < TimeSpan.FromDays(1) ? 20
            : age < TimeSpan.FromDays(7) ? 10
            : 0;
        return (Math.Min(record.Count, 20) * 4) + recency;
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, UsageRecord>>(File.ReadAllText(_path));
            if (loaded is not null)
            {
                _records = new Dictionary<string, UsageRecord>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.Warn($"Kullanım geçmişi okunamadı, sıfırdan başlanıyor: {ex.Message}");
        }
    }

    private void Save(Dictionary<string, UsageRecord> snapshot)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _log.Warn($"Kullanım geçmişi yazılamadı: {ex.Message}");
        }
    }
}
