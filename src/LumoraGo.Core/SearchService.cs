using System.Diagnostics;
using LumoraGo.Plugin;

namespace LumoraGo.Core;

public sealed record PluginTiming(string PluginId, TimeSpan Elapsed, PluginOutcome Outcome);

public enum PluginOutcome
{
    Ok,
    TimedOut,
    Failed,
}

public sealed record SearchResponse(
    Query Query,
    IReadOnlyList<Result> Results,
    TimeSpan Elapsed,
    IReadOnlyList<PluginTiming> Timings);

/// <summary>
/// Sorguyu ilgili eklentilere paralel dağıtır, sonuçları puana göre sıralar.
/// Her eklentinin bir zaman aşımı vardır; zaman aşan veya hata veren eklenti diğerlerini etkilemez.
/// </summary>
public sealed class SearchService
{
    public static readonly TimeSpan DefaultPluginTimeout = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan SlowQueryThreshold = TimeSpan.FromMilliseconds(30);

    private readonly PluginManager _plugins;
    private readonly ILog _log;
    private readonly TimeSpan _pluginTimeout;

    public SearchService(PluginManager plugins, ILog? log = null, TimeSpan? pluginTimeout = null)
    {
        _plugins = plugins;
        _log = log ?? NullLog.Instance;
        _pluginTimeout = pluginTimeout ?? DefaultPluginTimeout;
    }

    /// <exception cref="OperationCanceledException">Arama <paramref name="ct"/> ile iptal edildiğinde.</exception>
    public async Task<SearchResponse> SearchAsync(string? rawText, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = QueryParser.Parse(rawText, _plugins.ActionKeywords);
        var targets = SelectPlugins(query);

        var runs = targets.Select(entry => RunPluginAsync(entry, query, ct)).ToArray();
        var outcomes = await Task.WhenAll(runs).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // Eklenti sırasına göre kararlı sıralama: eşit puanda önce kaydedilen eklenti üstte.
        var results = outcomes
            .SelectMany(o => o.Results)
            .Select((r, i) => (r, i))
            .OrderByDescending(x => x.r.Score)
            .ThenBy(x => x.i)
            .Select(x => x.r)
            .ToList();

        stopwatch.Stop();
        return new SearchResponse(query, results, stopwatch.Elapsed, outcomes.Select(o => o.Timing).ToList());
    }

    private IEnumerable<PluginEntry> SelectPlugins(Query query)
    {
        if (query.ActionKeyword is { } keyword)
        {
            return _plugins.Plugins.Where(p =>
                string.Equals(p.Metadata.ActionKeyword, keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsEmpty)
        {
            return [];
        }

        return _plugins.Plugins.Where(p => p.Metadata.ActionKeyword is null);
    }

    private async Task<(IReadOnlyList<Result> Results, PluginTiming Timing)> RunPluginAsync(
        PluginEntry entry, Query query, CancellationToken ct)
    {
        var id = entry.Metadata.Id;
        var stopwatch = Stopwatch.StartNew();
        using var pluginCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // Task.Run: senkron bloklayan bir eklenti de UI'ı veya diğer eklentileri bekletmesin.
            var task = Task.Run(() => entry.Plugin.QueryAsync(query, pluginCts.Token), pluginCts.Token);
            var results = await task.WaitAsync(_pluginTimeout, ct).ConfigureAwait(false);
            stopwatch.Stop();

            if (stopwatch.Elapsed > SlowQueryThreshold)
            {
                _log.Warn($"Yavaş eklenti: {id} {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
            }

            var tagged = results.Select(r => r with { PluginId = id }).ToList();
            return (tagged, new PluginTiming(id, stopwatch.Elapsed, PluginOutcome.Ok));
        }
        catch (TimeoutException)
        {
            pluginCts.Cancel();
            _log.Warn($"Eklenti zaman aşımına uğradı ({_pluginTimeout.TotalMilliseconds:F0} ms): {id}");
            return ([], new PluginTiming(id, stopwatch.Elapsed, PluginOutcome.TimedOut));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ([], new PluginTiming(id, stopwatch.Elapsed, PluginOutcome.Failed));
        }
        catch (Exception ex)
        {
            _log.Error($"Eklenti sorguda hata verdi: {id}: {ex}");
            return ([], new PluginTiming(id, stopwatch.Elapsed, PluginOutcome.Failed));
        }
    }
}
