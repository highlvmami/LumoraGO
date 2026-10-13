using System.Text.Json;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Search;

/// <summary>
/// Hangi yazıda hangi sonucun seçildiğini öğrenir: "ch" yazıp hep Chrome'u seçiyorsan "c" ve "ch" yazınca Chrome öne çıkar.
/// Eski seçimlerin etkisi yarılanma süresiyle azalır, böylece alışkanlık değişince sıra da değişir.
/// Yalnızca eklentinin zaten döndürdüğü (yani yazılanla eşleşen) sonuçlar öne alınır.
/// </summary>
public sealed class QueryLearning
{
    public const int MaxEntries = 2000;
    public const int MaxBoost = 300;
    private const double PointsPerSelection = 60;

    private readonly string? _path;
    private readonly ILog _log;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _halfLife;
    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private long _version;
    private long _savedVersion;
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <param name="Query">Katlanmış (küçük harf, Türkçe harfler sadeleşmiş) yazı.</param>
    /// <param name="Key">Sonucun kararlı anahtarı (eklenti + başlık).</param>
    /// <param name="Weight">Son güncellemedeki ağırlık; okunurken yarılanmayla azaltılır.</param>
    public sealed record Entry(string Query, string Key, double Weight, DateTime UpdatedUtc);

    public QueryLearning(string? path, ILog? log = null, Func<DateTime>? utcNow = null, TimeSpan? halfLife = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _halfLife = halfLife ?? TimeSpan.FromDays(30);
        Load();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public static string KeyOf(Result result) => $"{result.PluginId}\u001f{result.Title}";

    public void Record(string rawText, Result result)
    {
        var query = Normalize(rawText);
        if (query.Length == 0 || result.PluginId is null)
        {
            return;
        }

        var key = KeyOf(result);
        var now = _utcNow();
        List<Entry> snapshot;
        long version;
        lock (_gate)
        {
            var id = query + "\u001e" + key;
            var weight = _entries.TryGetValue(id, out var old) ? Decayed(old, now) : 0;
            _entries[id] = new Entry(query, key, weight + 1, now);
            if (_entries.Count > MaxEntries)
            {
                foreach (var weakest in _entries.OrderBy(e => Decayed(e.Value, now)).Take(_entries.Count - MaxEntries).Select(e => e.Key).ToList())
                {
                    _entries.Remove(weakest);
                }
            }

            snapshot = [.. _entries.Values];
            version = ++_version;
        }

        _ = Task.Run(() => Save(version, snapshot));
    }

    /// <summary>Yazılan metin için sonuçların puanlarına eklenecek değerler. Kayıtlı yazı bununla başlıyorsa ya da bunun başıysa sayılır.</summary>
    public IReadOnlyDictionary<string, int> Boosts(string rawText)
    {
        var query = Normalize(rawText);
        var boosts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (query.Length == 0)
        {
            return boosts;
        }

        var now = _utcNow();
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Query.StartsWith(query, StringComparison.Ordinal) || query.StartsWith(entry.Query, StringComparison.Ordinal))
                {
                    // Tam aynı yazı en güçlü işarettir; yalnızca başı tutan yazılar yarım sayılır.
                    var weight = Decayed(entry, now) * (entry.Query == query ? 1 : 0.5);
                    sums[entry.Key] = sums.GetValueOrDefault(entry.Key) + weight;
                }
            }
        }

        foreach (var (key, sum) in sums)
        {
            var points = (int)Math.Round(Math.Min(MaxBoost, sum * PointsPerSelection));
            if (points > 0)
            {
                boosts[key] = points;
            }
        }

        return boosts;
    }

    public void Clear()
    {
        long version;
        lock (_gate)
        {
            _entries.Clear();
            version = ++_version;
        }

        _ = Task.Run(() => Save(version, []));
    }

    internal static string Normalize(string rawText) => FuzzyMatcher.Fold(rawText.Trim());

    private double Decayed(Entry entry, DateTime now) =>
        entry.Weight * Math.Pow(0.5, Math.Max(0, (now - entry.UpdatedUtc).TotalDays) / _halfLife.TotalDays);

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
            _entries = loaded
                .Where(e => e.Query is { Length: > 0 } && e.Key is { Length: > 0 })
                .GroupBy(e => e.Query + "\u001e" + e.Key)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Warn($"Öğrenilen sıralama okunamadı, sıfırdan başlanıyor: {ex.Message}");
        }
    }

    private void Save(long version, List<Entry> snapshot)
    {
        if (_path is null)
        {
            return;
        }

        // Kayıtlar arka planda sırasız bitebilir; daha yenisi yazıldıysa eskisi atlanır.
        lock (_saveGate)
        {
            if (version <= _savedVersion)
            {
                return;
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
                    File.Move(temp, _path, overwrite: true);
                    _savedVersion = version;
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Windows'ta dosya o an okunuyorsa üzerine yazılamaz; kısa bir süre sonra yeniden dene.
                    if (attempt == 5)
                    {
                        _log.Warn($"Öğrenilen sıralama yazılamadı: {ex.Message}");
                        return;
                    }

                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }
}
