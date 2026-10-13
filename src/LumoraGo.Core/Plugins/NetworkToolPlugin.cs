using LumoraGo.Core.Network;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// Ağ isteği yapan komutlar ("/ping", "/dns", "/whois", "/hiz", "/disip"). Her tuşta ağa gitmemek için iş Enter ile başlar;
/// sonuçlar geldikçe liste kendini tazeler. Yalnızca son iş tutulur, yenisi başlayınca eskisi iptal edilir.
/// </summary>
public sealed class NetworkToolPlugin : IPlugin
{
    private readonly string _actionLabel;
    private readonly string _example;
    private readonly NetLookup _lookup;
    private readonly bool _autoRun;
    private readonly bool _needsArgument;
    private readonly TimeSpan _keep;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private Job? _current;
    private IPluginHost? _host;

    private sealed class Job(string key, CancellationTokenSource cts)
    {
        public string Key { get; } = key;

        public CancellationTokenSource Cts { get; } = cts;

        public volatile IReadOnlyList<NetLine> Lines = [];

        public volatile string? Error;

        public volatile bool Done;

        public DateTime FinishedAt;
    }

    /// <param name="actionLabel">Başlatma satırı, örn. "Ping at".</param>
    /// <param name="example">Boş yazılınca gösterilen örnek.</param>
    /// <param name="autoRun">true ise komut yazılır yazılmaz (Enter beklemeden) başlar; tek seferlik, zararsız sorgular için.</param>
    /// <param name="needsArgument">false ise "/hiz" gibi argümansız çalışır.</param>
    /// <param name="keep">Biten sonuçlar bu süre sonra bayatlar ve komut yeniden yazılınca tekrar çalışır.</param>
    public NetworkToolPlugin(
        PluginMetadata metadata,
        string actionLabel,
        string example,
        NetLookup lookup,
        bool autoRun = false,
        bool needsArgument = true,
        TimeSpan? keep = null,
        Func<DateTime>? now = null)
    {
        Metadata = metadata;
        _actionLabel = actionLabel;
        _example = example;
        _lookup = lookup;
        _autoRun = autoRun;
        _needsArgument = needsArgument;
        _keep = keep ?? TimeSpan.FromMinutes(5);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public PluginMetadata Metadata { get; }

    private string Keyword => Metadata.ActionKeyword!;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var argument = query.Search.Trim();
        if (_needsArgument && argument.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([new Result($"{Metadata.Name}: bir şey yaz", $"Örnek: {_example}", null, 1, _ => false)]);
        }

        var key = argument.ToLowerInvariant();
        Job? job;
        lock (_gate)
        {
            job = _current;
            if (job is not null && (job.Key != key || (job.Done && _now() - job.FinishedAt > _keep)))
            {
                job = null;
            }
        }

        if (job is null)
        {
            if (!_autoRun)
            {
                var title = argument.Length == 0 ? _actionLabel : $"{_actionLabel}: {argument}";
                return Task.FromResult<IReadOnlyList<Result>>([new Result(title, "Enter ile başlat", null, 1000, _ =>
                {
                    Start(argument, key);
                    _host?.RefreshResults(Keyword);
                    return false;
                })]);
            }

            job = Start(argument, key);
        }

        return Task.FromResult<IReadOnlyList<Result>>(Rows(job));
    }

    private Job Start(string argument, string key)
    {
        var job = new Job(key, new CancellationTokenSource());
        lock (_gate)
        {
            _current?.Cts.Cancel();
            _current = job;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _lookup(argument, lines =>
                {
                    job.Lines = lines;
                    _host?.RefreshResults(Keyword);
                }, job.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Durduruldu ya da yeni iş başladı.
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                _host?.Log(LogLevel.Warning, $"{Keyword} başarısız: {ex.Message}");
            }
            finally
            {
                job.FinishedAt = _now();
                job.Done = true;
                _host?.RefreshResults(Keyword);
            }
        });
        return job;
    }

    private List<Result> Rows(Job job)
    {
        var results = new List<Result>();
        if (!job.Done)
        {
            results.Add(new Result("Çalışıyor…", "Enter ile durdur", null, 1000, _ =>
            {
                job.Cts.Cancel();
                return false;
            }));
        }

        var lines = job.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var copy = line.Copy ?? line.Title;
            results.Add(new Result(line.Title, line.Subtitle is { Length: > 0 } s ? $"{s} · Enter ile kopyala" : "Enter ile kopyala", null, 900 - i, _ =>
            {
                _host?.CopyToClipboard(copy);
                return true;
            }));
        }

        if (job.Error is { } error)
        {
            results.Add(new Result($"Hata: {error}", null, null, 10, _ => false));
        }
        else if (job.Done && lines.Count == 0)
        {
            results.Add(new Result("Sonuç yok", null, null, 10, _ => false));
        }

        return results;
    }
}

/// <summary>Hazır ağ aracı eklentileri.</summary>
public static class NetworkToolPlugins
{
    public static IReadOnlyList<NetworkToolPlugin> Create(NetworkTools tools, SpeedTest speed) =>
    [
        new(new PluginMetadata("lumorago.net.ping", "Ping", "/ping"), "Ping at", "/ping google.com", tools.PingAsync),
        new(new PluginMetadata("lumorago.net.dns", "DNS sorgusu", "/dns"), "DNS kayıtlarına bak", "/dns google.com, /dns google.com mx", tools.DnsAsync),
        new(new PluginMetadata("lumorago.net.whois", "Whois", "/whois"), "Alan adı bilgisine bak", "/whois google.com", tools.WhoisAsync),
        new(new PluginMetadata("lumorago.net.speed", "İnternet hız testi", "/hiz"), "İnternet hız testini başlat", "/hiz",
            (_, report, ct) => speed.RunAsync(report, ct), needsArgument: false),
        new(new PluginMetadata("lumorago.net.myip", "Dış IP ve konum", "/disip"), "Dış IP", "/disip",
            (_, report, ct) => tools.MyIpAsync(report, ct), autoRun: true, needsArgument: false, keep: TimeSpan.FromMinutes(1)),
    ];
}
