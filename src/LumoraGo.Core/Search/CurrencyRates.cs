using System.Text.Json;

namespace LumoraGo.Core.Search;

/// <summary>
/// Döviz kurları. İnternetten günde birkaç kez çekilir ve diske önbelleklenir; çevrimdışıyken son bilinen kurlar kullanılır.
/// Kaynak: open.er-api.com (anahtar gerektirmez), kurlar 1 USD karşılığıdır.
/// </summary>
public sealed class CurrencyRates
{
    public const string SourceUrl = "https://open.er-api.com/v6/latest/USD";
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private readonly string? _cachePath;
    private readonly Func<CancellationToken, Task<string>> _download;
    private readonly ILog _log;
    private volatile Snapshot? _snapshot;
    private int _refreshing;

    private sealed record Snapshot(IReadOnlyDictionary<string, double> PerUsd, DateTime UpdatedUtc);

    /// <param name="cachePath">Son kurların saklandığı dosya; null ise önbellek yok.</param>
    /// <param name="download"><see cref="SourceUrl"/> içeriğini indirir.</param>
    public CurrencyRates(string? cachePath, Func<CancellationToken, Task<string>> download, ILog? log = null)
    {
        _cachePath = cachePath;
        _download = download;
        _log = log ?? NullLog.Instance;
        if (cachePath is not null && File.Exists(cachePath))
        {
            try
            {
                _snapshot = Parse(File.ReadAllText(cachePath), File.GetLastWriteTimeUtc(cachePath));
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException)
            {
                _log.Warn($"Kur önbelleği okunamadı: {ex.Message}");
            }
        }
    }

    public bool IsLoaded => _snapshot is not null;

    public DateTime? UpdatedUtc => _snapshot?.UpdatedUtc;

    /// <summary>Kurlar henüz yüklenmediyse de yaygın kodları tanır ki "100 usd try" yazınca "kurlar yükleniyor" gösterilebilsin.</summary>
    public bool IsKnown(string code) =>
        _snapshot?.PerUsd.ContainsKey(code) ?? Common.Contains(code);

    public bool TryConvert(double amount, string from, string to, out double result)
    {
        result = 0;
        if (_snapshot is not { } s || !s.PerUsd.TryGetValue(from, out var fromRate) || !s.PerUsd.TryGetValue(to, out var toRate))
        {
            return false;
        }

        result = amount / fromRate * toRate;
        return true;
    }

    /// <summary>Kurlar yoksa veya eskiyse arka planda yeniler. Aynı anda tek yenileme çalışır.</summary>
    public async Task RefreshIfStaleAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (_snapshot is { } s && nowUtc - s.UpdatedUtc < MaxAge)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var json = await _download(ct).ConfigureAwait(false);
            _snapshot = Parse(json, nowUtc);
            if (_cachePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                await File.WriteAllTextAsync(_cachePath, json, ct).ConfigureAwait(false);
            }

            _log.Info($"Döviz kurları güncellendi ({_snapshot.PerUsd.Count} para birimi)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"Döviz kurları alınamadı: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private static Snapshot Parse(string json, DateTime updatedUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Kur listesi yok");
        }

        var perUsd = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var rate in rates.EnumerateObject())
        {
            if (rate.Value.TryGetDouble(out var value) && value > 0)
            {
                perUsd[rate.Name] = value;
            }
        }

        return new Snapshot(perUsd, updatedUtc);
    }

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "TRY", "GBP", "JPY", "CHF", "RUB", "CNY", "CAD", "AUD", "SAR", "AED", "AZN", "SEK", "NOK", "DKK",
    };
}
