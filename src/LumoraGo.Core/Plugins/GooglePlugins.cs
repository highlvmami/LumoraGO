using System.Globalization;
using LumoraGo.Core.Actions;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Google;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/görev": Google Görevler'deki yapılacaklar (tüm listeler), tarihi yakın olan başta. Yazınca süzülür ve en üstte
/// "Ekle" satırı çıkar ("/görev yarın fatura öde" yarına tarihli ekler). Enter görevi tamamlar.
/// </summary>
public sealed class TasksPlugin : IPlugin
{
    public const string Keyword = "/görev";
    public const string TasksUrl = "https://tasks.google.com/";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(1);

    private readonly GoogleTasks _tasks;
    private readonly Action<string> _open;
    private readonly Action? _openSettings;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private IPluginHost? _host;
    private IReadOnlyList<TaskItem>? _items;
    private string? _error;
    private DateTime _loadedAt;
    private bool _loading;

    public TasksPlugin(GoogleTasks tasks, Action<string> open, Action? openSettings = null, string keyword = Keyword, Func<DateTime>? now = null)
    {
        _tasks = tasks;
        _open = open;
        _openSettings = openSettings;
        _now = now ?? (() => DateTime.Now);
        Metadata = new PluginMetadata("lumorago.tasks." + keyword.Trim('/'), "Google Görevler", keyword);
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!_tasks.IsConnected)
        {
            return One(NotConnected("Google Görevler", _openSettings));
        }

        var text = query.Search.Trim();
        IReadOnlyList<TaskItem>? items;
        string? error;
        lock (_gate)
        {
            items = _items;
            error = _error;
            if (items is null || _now() - _loadedAt > Keep)
            {
                StartLoad();
            }
        }

        var results = new List<Result>();
        if (text.Length > 0)
        {
            results.Add(AddRow(text));
        }

        if (error is not null)
        {
            results.Add(new Result($"Görevler okunamadı — {error}", "Enter ile Ayarlar'ı aç", null, 5, _ =>
            {
                _openSettings?.Invoke();
                return true;
            }));
        }

        if (items is null)
        {
            results.Add(new Result("Görevler yükleniyor…", null, null, 1, _ => false));
            return Task.FromResult<IReadOnlyList<Result>>(results);
        }

        var folded = FuzzyMatcher.Fold(text);
        var shown = items
            .Where(t => folded.Length == 0 || FuzzyMatcher.Score(folded, t.Title + " " + t.Notes) > 0)
            .OrderBy(t => t.Due ?? DateTime.MaxValue)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            results.Add(Row(shown[i], 1000 - i));
        }

        if (shown.Count == 0 && text.Length == 0)
        {
            results.Add(new Result("Yapılacak görev yok", "Eklemek için yaz: /görev yarın fatura öde", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private Result AddRow(string text)
    {
        var now = _now();
        var date = TurkishDate.Parse(text, now) is { Rest.Length: > 0 } d ? d : null;
        var title = date?.Rest ?? text;
        var due = date?.When.Date;
        var when = date is null ? "tarihsiz" : date.Describe(now);
        return new Result($"Görev ekle: {title}", $"{when} · Google Görevler'e eklenir · Enter", null, 2000, ctx =>
        {
            _ = Run(() => _tasks.AddAsync(title, due, CancellationToken.None), $"Eklendi: {title}");
            return true;
        });
    }

    private Result Row(TaskItem task, int score)
    {
        var due = task.Due is { } d ? Due(d, _now().Date) : null;
        var subtitle = string.Join(" · ", new[] { due, task.ListTitle, "Enter ile tamamla" }.Where(s => !string.IsNullOrEmpty(s)));
        return new Result(task.Title, subtitle, null, score, ctx =>
        {
            lock (_gate)
            {
                _items = _items?.Where(t => t != task).ToList();
            }

            _ = Run(() => _tasks.CompleteAsync(task, CancellationToken.None), $"Tamamlandı: {task.Title}");
            return false;
        }, [
            new ContextAction("Google Görevler'de aç", _ => { _open(TasksUrl); return true; }),
            new ContextAction("Kopyala", _ => { _host?.CopyToClipboard(task.Title); return true; }),
        ])
        {
            PreviewText = string.IsNullOrWhiteSpace(task.Notes) ? null : $"{task.Title}\n\n{task.Notes}",
        };
    }

    internal static string Due(DateTime due, DateTime today) => (due - today).Days switch
    {
        < 0 => $"Gecikti ({due.ToString("d MMMM", Turkish)})",
        0 => "Bugün",
        1 => "Yarın",
        < 7 => due.ToString("dddd", Turkish),
        _ => due.ToString("d MMMM", Turkish),
    };

    private async Task Run(Func<Task> action, string done)
    {
        try
        {
            await action();
            _host?.ShowMessage("Google Görevler", done);
        }
        catch (GoogleException ex)
        {
            _host?.ShowMessage("Google Görevler", ex.Message);
        }

        lock (_gate)
        {
            _loadedAt = DateTime.MinValue;
        }

        _host?.RefreshResults(Metadata.ActionKeyword!);
    }

    private void StartLoad()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var items = await _tasks.GetAsync(CancellationToken.None);
                lock (_gate)
                {
                    _items = items;
                    _error = null;
                }
            }
            catch (GoogleException ex)
            {
                lock (_gate)
                {
                    _items ??= [];
                    _error = ex.Message;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _loadedAt = _now();
                    _loading = false;
                }

                _host?.RefreshResults(Metadata.ActionKeyword!);
            }
        });
    }

    internal static Result NotConnected(string service, Action? openSettings) =>
        new($"{service} bağlı değil", "Enter ile Ayarlar'ı aç ve \"Google ile bağlan\"a bas", null, 1000, _ =>
        {
            openSettings?.Invoke();
            return true;
        });

    private static Task<IReadOnlyList<Result>> One(Result result) => Task.FromResult<IReadOnlyList<Result>>([result]);
}

/// <summary>"/drive": Google Drive'da dosya adına göre arar; boşken son açılanlar. Enter tarayıcıda açar.</summary>
public static class DriveSearch
{
    public const string Keyword = "/drive";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static LiveLookupPlugin Create(GoogleDrive drive, Func<bool> connected, Action<string> open, Action? openSettings = null) => new(
        new PluginMetadata("lumorago.drive", "Google Drive", Keyword),
        "Dosya adını yaz: /drive bütçe",
        async (text, host, ct) =>
        {
            if (!connected())
            {
                return [TasksPlugin.NotConnected("Google Drive", openSettings)];
            }

            var files = await drive.SearchAsync(text, ct);
            return files.Select((f, i) => new Result(
                f.Name,
                $"{f.Kind}{(f.Modified is { } m ? " · " + m.ToString("d MMMM yyyy", Turkish) : "")} · Enter ile aç",
                null,
                1000 - i,
                _ =>
                {
                    open(f.Url);
                    return true;
                },
                [new ContextAction("Bağlantıyı kopyala", _ => { host?.CopyToClipboard(f.Url); return true; })])
            {
                DragData = DragPayload.Url(f.Url),
            }).ToList();
        },
        needsArgument: false,
        cacheFor: TimeSpan.FromMinutes(1));
}

/// <summary>
/// "/youtube": abone olduğun kanallar, oynatma listelerin ve beğendiğin videolar içinde arar; en altta YouTube'da arama
/// satırı da çıkar. Liste açılışta ve birkaç saatte bir kendiliğinden yenilenir; "/youtube yenile" hemen yeniler.
/// </summary>
public sealed class YouTubePlugin : IPlugin
{
    public const string Keyword = "/youtube";
    private const int MaxResults = 200;
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);

    private readonly YouTubeLibrary _library;
    private readonly Action<string> _open;
    private readonly Action? _openSettings;
    private IPluginHost? _host;
    private string? _error;
    private DateTime _lastAttempt = DateTime.MinValue;

    public YouTubePlugin(YouTubeLibrary library, Action<string> open, Action? openSettings = null)
    {
        _library = library;
        _open = open;
        _openSettings = openSettings;
    }

    public static readonly PluginMetadata Metadata = new("lumorago.youtube.library", "YouTube kütüphanen", Keyword);

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!_library.IsConnected)
        {
            return Task.FromResult<IReadOnlyList<Result>>([TasksPlugin.NotConnected("YouTube", _openSettings)]);
        }

        var text = query.Search.Trim();
        var folded = FuzzyMatcher.Fold(text);
        // Başarısız yenileme her tuşta yeniden denenmesin; "yenile" de yenileme bitince tekrar tetiklenmesin.
        var sinceAttempt = DateTime.UtcNow - _lastAttempt;
        if ((folded == "yenile" && sinceAttempt > TimeSpan.FromSeconds(30))
            || ((_library.RefreshedAt is not { } at || DateTime.UtcNow - at > RefreshEvery) && sinceAttempt > TimeSpan.FromMinutes(2)))
        {
            StartRefresh();
        }

        var results = new List<Result>();
        if (folded == "yenile")
        {
            results.Add(new Result(_library.IsRefreshing ? "YouTube kütüphanen yenileniyor…" : $"YouTube kütüphanen güncel · {_library.Items.Count} öğe", null, null, 1000, _ => false));
            return Task.FromResult<IReadOnlyList<Result>>(results);
        }

        if (_error is { } error)
        {
            results.Add(new Result($"YouTube okunamadı — {error}", "Enter ile Ayarlar'ı aç", null, 2000, _ =>
            {
                _openSettings?.Invoke();
                return true;
            }));
        }

        var items = _library.Items;
        if (items.Count == 0)
        {
            results.Add(new Result(_library.IsRefreshing ? "YouTube kütüphanen yükleniyor…" : "YouTube kütüphanen boş", null, null, 1, _ => false));
        }

        var matches = items
            .Select((item, index) => (Item: item, Score: folded.Length == 0 ? MaxResults - index : FuzzyMatcher.Score(folded, item.Title)))
            .Where(m => m.Score > 0)
            .OrderBy(m => folded.Length == 0 ? (int)m.Item.Kind : 0)
            .ThenByDescending(m => m.Score)
            .Take(MaxResults)
            .ToList();
        for (var i = 0; i < matches.Count; i++)
        {
            var item = matches[i].Item;
            results.Add(new Result(item.Title, item.Detail, null, 1000 - i, _ =>
            {
                _open(item.Url);
                return true;
            }, [new ContextAction("Bağlantıyı kopyala", _ => { _host?.CopyToClipboard(item.Url); return true; })])
            {
                DragData = DragPayload.Url(item.Url),
            });
        }

        if (text.Length > 0)
        {
            var url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text);
            results.Add(new Result($"YouTube'da ara: {text}", "Tarayıcıda açılır", null, 0, _ =>
            {
                _open(url);
                return true;
            }));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private void StartRefresh()
    {
        if (_library.IsRefreshing)
        {
            return;
        }

        _lastAttempt = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await _library.RefreshAsync(CancellationToken.None);
                _error = null;
            }
            catch (GoogleException ex)
            {
                _error = ex.Message;
            }

            _host?.RefreshResults(Keyword);
        });
    }
}
