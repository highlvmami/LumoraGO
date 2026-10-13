using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Arka planda ağa gidip sonuç üreten sorgu: yazılan metin ve eklentiyle iletişim için host.</summary>
public delegate Task<IReadOnlyList<Result>> LiveLookup(string argument, IPluginHost? host, CancellationToken ct);

/// <summary>
/// Yazarken ağdan bilgi getiren komutlar ("/tanım", "/hava", "/hisse", "/mac"). Yazmayı bekleyip (her harfte sormaz)
/// arka planda sorgular, gelene kadar "yükleniyor" gösterir, gelince listeyi tazeler. Sonuçlar bir süre önbellekte kalır.
/// </summary>
public sealed class LiveLookupPlugin : IPlugin
{
    private const int MaxCached = 50;
    private static readonly TimeSpan ErrorCacheFor = TimeSpan.FromSeconds(30);

    private readonly LiveLookup _lookup;
    private readonly string _hint;
    private readonly bool _needsArgument;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _cacheFor;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<Result>? Results, string? Error)> _cache = [];
    private (string Key, CancellationTokenSource Cts)? _running;
    private IPluginHost? _host;

    /// <param name="hint">Argüman gerekirken boş yazılınca gösterilen örnek.</param>
    public LiveLookupPlugin(
        PluginMetadata metadata,
        string hint,
        LiveLookup lookup,
        bool needsArgument = true,
        TimeSpan? debounce = null,
        TimeSpan? cacheFor = null,
        Func<DateTime>? now = null)
    {
        Metadata = metadata;
        _hint = hint;
        _lookup = lookup;
        _needsArgument = needsArgument;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
        _cacheFor = cacheFor ?? TimeSpan.FromMinutes(10);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var argument = query.Search.Trim();
        if (_needsArgument && argument.Length == 0)
        {
            return One(new Result(Metadata.Name, _hint, null, 1, _ => false));
        }

        var key = FuzzyMatcher.Fold(argument);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && _now() - hit.At < (hit.Error is null ? _cacheFor : ErrorCacheFor))
            {
                return hit.Error is { } error
                    ? One(new Result($"Bilgi alınamadı — {error}", "İnternet bağlantını kontrol et ya da biraz sonra tekrar dene", null, 1, _ => false))
                    : Task.FromResult(hit.Results!.Count > 0
                        ? hit.Results
                        : (IReadOnlyList<Result>)[new Result("Sonuç bulunamadı", _hint, null, 1, _ => false)]);
            }

            if (_running is not { } running || running.Key != key)
            {
                _running?.Cts.Cancel();
                var cts = new CancellationTokenSource();
                _running = (key, cts);
                _ = Run(argument, key, cts);
            }
        }

        return One(new Result($"{Metadata.Name}: yükleniyor…", argument.Length > 0 ? argument : null, null, 1, _ => false));
    }

    private async Task Run(string argument, string key, CancellationTokenSource cts)
    {
        IReadOnlyList<Result>? results = null;
        string? error = null;
        try
        {
            await Task.Delay(_debounce, cts.Token).ConfigureAwait(false);
            results = await _lookup(argument, _host, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            error = ex is TaskCanceledException ? "zaman aşımı" : ex.Message;
            _host?.Log(LogLevel.Warning, $"{Metadata.ActionKeyword} \"{argument}\" başarısız: {ex.Message}");
        }

        lock (_gate)
        {
            if (_cache.Count >= MaxCached)
            {
                foreach (var old in _cache.OrderBy(c => c.Value.At).Take(_cache.Count - MaxCached + 1).Select(c => c.Key).ToList())
                {
                    _cache.Remove(old);
                }
            }

            _cache[key] = (_now(), results, error);
            if (_running is { } running && running.Key == key)
            {
                _running = null;
            }
        }

        _host?.RefreshResults(Metadata.ActionKeyword!);
    }

    private static Task<IReadOnlyList<Result>> One(Result result) => Task.FromResult<IReadOnlyList<Result>>([result]);
}
