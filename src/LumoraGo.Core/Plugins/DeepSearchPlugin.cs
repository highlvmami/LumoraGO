using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Files;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/ds şifre": tüm bilgisayarda dosya adlarında ve dosyaların içinde arar. Yazmayı bırakınca kendiliğinden başlar,
/// sonuçlar bulundukça listeye eklenir. Kelime değişince eski arama durur.
/// </summary>
public sealed class DeepSearchPlugin : IPlugin
{
    public const string Keyword = "/ds";
    public static readonly PluginMetadata Metadata = new("lumorago.deepsearch", "Derin arama (deepsearch)", Keyword);

    public static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);
    private const int MinTermLength = 2;
    private const int HeaderScore = 100_000;

    private readonly Func<DeepSearchOptions> _options;
    private readonly FileResults _files;
    private readonly object _gate = new();
    private IPluginHost? _host;
    private DeepSearchJob? _job;
    private Timer? _refreshTimer;

    /// <param name="options">Her yeni aramada çağrılır; ayarlarda değişen klasörler hemen geçerli olur.</param>
    /// <param name="open">Dosyayı varsayılan uygulamasıyla açar.</param>
    /// <param name="reveal">Dosyayı Gezgin'de seçili olarak gösterir.</param>
    public DeepSearchPlugin(Func<DeepSearchOptions> options, Action<string> open, Action<string> reveal)
    {
        _options = options;
        _files = new FileResults(open, reveal);
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
        _files.Init(context.Host);
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var term = query.Search;
        if (term.Length < MinTermLength)
        {
            return Task.FromResult<IReadOnlyList<Result>>(
            [
                new Result("Derin arama", "\"/ds kelime\" yaz: tüm bilgisayarda dosya adlarında ve içlerinde arar", null, HeaderScore, _ => false),
            ]);
        }

        DeepSearchJob job;
        lock (_gate)
        {
            if (_job is null || _job.Term != term)
            {
                _job?.Stop();
                _job = DeepSearchJob.Start(term, _options(), StartDelay);
                StartRefreshing(_job);
            }

            job = _job;
        }

        return Task.FromResult(BuildResults(job));
    }

    /// <summary>Çalışan aramayı durdurur (örn. launcher kapanırken).</summary>
    public void StopSearch()
    {
        lock (_gate)
        {
            _job?.Stop();
        }
    }

    private IReadOnlyList<Result> BuildResults(DeepSearchJob job)
    {
        var snapshot = job.Snapshot();
        var results = new List<Result>(snapshot.Hits.Count + 1) { Header(job, snapshot) };

        // İsim eşleşmeleri önce, sonra içerik eşleşmeleri; her grup bulunma sırasıyla.
        var ordered = snapshot.Hits
            .Select((hit, i) => (hit, i))
            .OrderBy(x => x.hit.Kind)
            .ThenBy(x => x.i)
            .Select(x => x.hit)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            results.Add(ToResult(ordered[i], HeaderScore - 1 - i));
        }

        return results;
    }

    private Result Header(DeepSearchJob job, DeepSearchSnapshot s)
    {
        var count = $"{s.FilesScanned:N0} dosya tarandı, {s.Hits.Count} sonuç";
        if (s.IsRunning)
        {
            var where = s.CurrentFolder is { } folder ? $" · {folder}" : "";
            return new Result($"Aranıyor: \"{s.Term}\"", $"{count}{where} · Enter: durdur", null, HeaderScore, _ =>
            {
                job.Stop();
                _host?.RefreshResults(Keyword);
                return false;
            });
        }

        var end = s.HitLimitReached
            ? $"İlk {s.Hits.Count} sonuçta durdu, daha belirgin bir kelime dene"
            : s.WasStopped ? "Durduruldu" : $"Bitti ({s.Elapsed.TotalSeconds:F0} sn)";
        return new Result($"Derin arama: \"{s.Term}\"", $"{end} · {count}", null, HeaderScore, _ => false);
    }

    private Result ToResult(DeepSearchHit hit, int score)
    {
        var name = Path.GetFileName(hit.Path.TrimEnd(Path.DirectorySeparatorChar));
        var subtitle = hit.Kind == DeepSearchMatchKind.Name
            ? $"{(hit.IsDirectory ? "Klasör" : "Dosya")} adında geçiyor · {hit.Path}"
            : $"Satır {hit.Line}{(hit.ContentMatches > 1 ? $" (+{hit.ContentMatches - 1} satır daha)" : "")}: {hit.Snippet} · {hit.Path}";

        return _files.Create(hit.Path, hit.IsDirectory, name, subtitle, score);
    }

    /// <summary>Arama sürerken listeyi düzenli tazeler, bitince son bir kez tazeleyip durur.</summary>
    private void StartRefreshing(DeepSearchJob job)
    {
        _refreshTimer?.Dispose();
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            _host?.RefreshResults(Keyword);
            if (job.Completion.IsCompleted)
            {
                timer?.Dispose();
            }
        }, null, RefreshInterval, RefreshInterval);
        _refreshTimer = timer;
    }
}
