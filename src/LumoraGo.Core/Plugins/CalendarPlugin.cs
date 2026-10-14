using System.Globalization;
using LumoraGo.Core.Calendar;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/takvim": bugünün kalan toplantıları; "/takvim yarın" ve "/takvim hafta" ile ilerisi. Enter toplantı bağlantısını açar
/// (yoksa başlığı kopyalar). Takvimi ağdan almak 200 ms'yi aşabildiği için ilk açılışta "yükleniyor" gösterilip liste tazelenir.
/// </summary>
public sealed class CalendarPlugin : IPlugin
{
    public const string Keyword = "/takvim";
    public static readonly PluginMetadata Metadata = new("lumorago.calendar", "Takvim", Keyword);

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private const int Days = 8;

    private readonly ICalendarSource _source;
    private readonly Action<string> _open;
    private readonly Action _openSettings;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _keep;
    private readonly object _gate = new();
    private IPluginHost? _host;
    private IReadOnlyList<CalendarItem>? _items;
    private IReadOnlyList<string> _errors = [];
    private DateTime _loadedAt;
    private DateTime _loadedFor;
    private bool _loading;

    /// <param name="open">Toplantı bağlantısını tarayıcıda açar.</param>
    /// <param name="openSettings">Takvim bağlı değilken Ayarlar penceresini açar.</param>
    public CalendarPlugin(ICalendarSource source, Action<string> open, Action openSettings, Func<DateTime>? now = null, TimeSpan? keep = null)
    {
        _source = source;
        _open = open;
        _openSettings = openSettings;
        _now = now ?? (() => DateTime.Now);
        _keep = keep ?? TimeSpan.FromMinutes(2);
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!_source.IsConfigured)
        {
            return One(new Result("Takvim bağlı değil", "Enter ile Ayarlar'ı aç: Google ile bağlan ya da gizli takvim adresini (ICS) yapıştır", null, 1000, _ =>
            {
                _openSettings();
                return true;
            }));
        }

        var now = _now();
        IReadOnlyList<CalendarItem>? items;
        IReadOnlyList<string> errors;
        bool stale;
        lock (_gate)
        {
            items = _items;
            errors = _errors;
            stale = items is null || now - _loadedAt > _keep || _loadedFor != now.Date;
        }

        if (stale)
        {
            StartLoad(now);
        }

        if (items is null)
        {
            return One(new Result("Takvim yükleniyor…", null, null, 1000, _ => false));
        }

        var mode = Mode(query.Search);
        if (mode is null)
        {
            return One(new Result("Anlaşılmadı", "/takvim, /takvim yarın ya da /takvim hafta yaz", null, 1, _ => false));
        }

        var results = Build(items, mode.Value, now);
        foreach (var error in errors)
        {
            results.Add(new Result($"Takvim okunamadı — {error}", "Adresi Ayarlar'dan kontrol et", null, 5, _ =>
            {
                _openSettings();
                return true;
            }));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private enum View
    {
        Today,
        Tomorrow,
        Week,
    }

    private static View? Mode(string search) => FuzzyMatcher.Fold(search.Trim()) switch
    {
        "" or "bugun" => View.Today,
        "yarin" => View.Tomorrow,
        "hafta" or "haftalik" => View.Week,
        _ => null,
    };

    private void StartLoad(DateTime now)
    {
        lock (_gate)
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
        }

        _ = Task.Run(async () =>
        {
            var errors = new List<string>();
            try
            {
                var items = await _source.GetAsync(now.Date, now.Date.AddDays(Days), errors.Add, CancellationToken.None);
                lock (_gate)
                {
                    _items = items;
                    _errors = errors;
                    _loadedAt = _now();
                    _loadedFor = now.Date;
                }
            }
            catch (Exception ex)
            {
                _host?.Log(LogLevel.Warning, $"{Keyword} başarısız: {ex.Message}");
                lock (_gate)
                {
                    _items ??= [];
                    _errors = [ex.Message];
                    _loadedAt = _now();
                    _loadedFor = now.Date;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _loading = false;
                }

                _host?.RefreshResults(Keyword);
            }
        });
    }

    private List<Result> Build(IReadOnlyList<CalendarItem> items, View view, DateTime now)
    {
        var today = now.Date;
        var (from, to, label) = view switch
        {
            View.Tomorrow => (today.AddDays(1), today.AddDays(2), "yarın"),
            View.Week => (today, today.AddDays(7), "bu hafta"),
            _ => (today, today.AddDays(1), "bugün"),
        };

        var shown = items
            .Where(i => i.Start < to && (i.End > from || i.Start >= from))
            .Where(i => view != View.Today || i.AllDay || i.End > now || i.Start >= now)
            .ToList();

        var results = new List<Result>();
        for (var i = 0; i < shown.Count; i++)
        {
            var item = shown[i];
            results.Add(new Result(Title(item, view, now), Subtitle(item), null, 1000 - i, _ =>
            {
                if (item.MeetingUrl is { } url)
                {
                    _open(url);
                }
                else
                {
                    _host?.CopyToClipboard(item.Title);
                }

                return true;
            })
            {
                DragData = item.MeetingUrl is { } meeting ? DragPayload.Url(meeting) : DragPayload.Text(item.Title),
            });
        }

        if (results.Count == 0)
        {
            results.Add(new Result(view == View.Today ? "Bugün başka etkinlik yok" : $"{Capitalize(label)} etkinlik yok", null, null, 1, _ => false));
        }

        return results;
    }

    internal static string Title(CalendarItem item, bool showDay, DateTime now)
    {
        var day = showDay ? $"{item.Start.ToString("ddd", Turkish)} " : "";
        if (item.AllDay)
        {
            return $"{day}Tüm gün · {item.Title}";
        }

        var running = item.Start <= now && item.End > now;
        return $"{day}{(running ? "şimdi" : item.Start.ToString("HH:mm", Turkish))}–{item.End.ToString("HH:mm", Turkish)} · {item.Title}";
    }

    private static string Title(CalendarItem item, View view, DateTime now) => Title(item, view == View.Week, now);

    private static string Subtitle(CalendarItem item)
    {
        var action = item.MeetingUrl is null ? "Enter ile başlığı kopyala" : "Enter ile toplantıya katıl";
        return item.Location is { } location && item.MeetingUrl is null ? $"{location} · {action}" : action;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Turkish) + text[1..];

    private static Task<IReadOnlyList<Result>> One(Result result) => Task.FromResult<IReadOnlyList<Result>>([result]);
}
