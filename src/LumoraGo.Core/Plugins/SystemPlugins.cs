using System.Globalization;
using LumoraGo.Core.Desktop;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Bilgisayar yönetimi: "/sistem" (işlemci, bellek, disk, pil), "/temizle" ve "/başlangıç".</summary>
public static class SystemPlugins
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<PluginEntry> Create(
        Func<CancellationToken, Task<SystemSnapshot>> snapshot,
        Func<IReadOnlyList<CleanupTarget>> cleanupTargets,
        Func<IReadOnlyList<StartupItem>> startupItems,
        Func<StartupItem, bool, string?> setStartupEnabled,
        Action openTaskManager,
        Action openStartupSettings)
    {
        LiveLookup system = (_, host, ct) => SystemAsync(snapshot, host, openTaskManager, ct);
        LiveLookup cleanup = (_, host, ct) => Task.Run(() => Cleanup(cleanupTargets(), host), ct);
        var systemPlugin = new LiveLookupPlugin(new PluginMetadata("lumorago.system", "Sistem durumu", "/sistem"), "", system,
            needsArgument: false, debounce: TimeSpan.Zero, cacheFor: TimeSpan.FromSeconds(2));
        var cleanupPlugin = new LiveLookupPlugin(new PluginMetadata("lumorago.cleanup", "Disk temizliği", "/temizle"), "", cleanup,
            needsArgument: false, debounce: TimeSpan.Zero, cacheFor: TimeSpan.FromSeconds(5));
        var startup = new StartupPlugin("/başlangıç", startupItems, setStartupEnabled, openStartupSettings);
        var startupAscii = new StartupPlugin("/baslangic", startupItems, setStartupEnabled, openStartupSettings);
        return
        [
            new PluginEntry(systemPlugin.Metadata, systemPlugin),
            new PluginEntry(cleanupPlugin.Metadata, cleanupPlugin),
            new PluginEntry(startup.Metadata, startup),
            new PluginEntry(startupAscii.Metadata, startupAscii),
        ];
    }

    // ---- Sistem durumu ----

    internal static async Task<IReadOnlyList<Result>> SystemAsync(
        Func<CancellationToken, Task<SystemSnapshot>> snapshot, IPluginHost? host, Action openTaskManager, CancellationToken ct)
    {
        var s = await snapshot(ct).ConfigureAwait(false);
        bool TaskManager(ActionContext _)
        {
            openTaskManager();
            return true;
        }

        var memoryPercent = s.MemoryTotalBytes > 0 ? s.MemoryUsedBytes * 100.0 / s.MemoryTotalBytes : 0;
        var results = new List<Result>
        {
            new("İşlemci", "Enter ile Görev Yöneticisi", InfoPlugins.EmojiPrefix + "🧠", 1000, TaskManager)
            {
                Card = new ResultCard(Percent(s.CpuPercent), "İşlemci kullanımı", [$"Açık kalma süresi: {Uptime(s.Uptime)}"], Positive: Load(s.CpuPercent)),
            },
            new("Bellek", "Enter ile Görev Yöneticisi", InfoPlugins.EmojiPrefix + "💾", 990, TaskManager)
            {
                Card = new ResultCard(
                    $"{Gb(s.MemoryUsedBytes)} / {Gb(s.MemoryTotalBytes)}",
                    $"{Percent(memoryPercent)} dolu · {Gb(s.MemoryTotalBytes - s.MemoryUsedBytes)} boş",
                    Positive: Load(memoryPercent)),
            },
        };

        foreach (var (disk, i) in s.Disks.Select((d, i) => (d, i)))
        {
            var label = disk.Label.Length > 0 ? $"{disk.Name} {disk.Label}" : disk.Name;
            results.Add(new Result(
                $"{label} · {ProcessKillPlugin.FormatBytes(disk.FreeBytes)} boş / {ProcessKillPlugin.FormatBytes(disk.TotalBytes)}",
                $"{Percent(disk.UsedPercent)} dolu{(disk.UsedPercent >= 90 ? " · yer azalmış, /temizle dene" : "")}",
                InfoPlugins.EmojiPrefix + "🗄️", 980 - i, _ =>
                {
                    host?.ChangeQuery("/temizle");
                    return false;
                }));
        }

        if (s.Battery is { } battery)
        {
            var state = battery.Charging ? "şarj oluyor" : battery.Remaining is { } left ? $"yaklaşık {Uptime(left)} kaldı" : "pilde";
            results.Add(new Result($"Pil %{battery.Percent} · {state}", null, InfoPlugins.EmojiPrefix + (battery.Charging ? "🔌" : "🔋"), 970, _ => false));
        }

        foreach (var (process, i) in s.TopProcesses.Take(5).Select((p, i) => (p, i)))
        {
            results.Add(new Result($"{process.Name} · {ProcessKillPlugin.FormatBytes(process.MemoryBytes)}", "En çok bellek kullananlar · Enter ile kapatma listesine git",
                process.ExePath, 900 - i, _ =>
                {
                    host?.ChangeQuery($"{ProcessKillPlugin.Keyword} {process.Name}");
                    return false;
                }));
        }

        return results;
    }

    private static bool? Load(double percent) => percent >= 85 ? false : percent < 60 ? true : null;

    private static string Percent(double value) => "%" + Math.Round(value).ToString(Turkish);

    private static string Gb(long bytes) => (bytes / (double)(1L << 30)).ToString("0.0", Turkish) + " GB";

    internal static string Uptime(TimeSpan span) => span.TotalDays >= 1
        ? $"{(int)span.TotalDays} gün {span.Hours} sa"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} sa {span.Minutes} dk" : $"{Math.Max(1, span.Minutes)} dk";

    // ---- Temizlik ----

    internal static IReadOnlyList<Result> Cleanup(IReadOnlyList<CleanupTarget> targets, IPluginHost? host)
    {
        var measured = targets.Select(t => (Target: t, Size: Safe(t.Measure))).ToList();
        var cleanable = measured.Where(m => m.Target.Clean is not null && m.Size > 0).ToList();
        var results = new List<Result>();
        if (cleanable.Count > 1)
        {
            var total = cleanable.Sum(m => m.Size);
            results.Add(new Result($"Hepsini temizle · {ProcessKillPlugin.FormatBytes(total)}",
                string.Join(", ", cleanable.Select(c => c.Target.Name)) + " · İndirilenler'e dokunulmaz",
                InfoPlugins.EmojiPrefix + "🧹", 1000, _ =>
                {
                    Run(host, cleanable.Select(c => c.Target).ToList());
                    return true;
                }));
        }

        foreach (var ((target, size), i) in measured.Select((m, i) => (m, i)))
        {
            var sizeText = ProcessKillPlugin.FormatBytes(size);
            if (target.Clean is null)
            {
                results.Add(new Result(size > 0 ? $"{target.Name} · {sizeText}" : target.Name, $"{target.Hint} · Enter ile aç", InfoPlugins.EmojiPrefix + target.Emoji, 900 - i, _ =>
                {
                    target.Open?.Invoke();
                    return true;
                }));
            }
            else if (size == 0)
            {
                results.Add(new Result($"{target.Name} · boş", target.Hint, InfoPlugins.EmojiPrefix + target.Emoji, 900 - i, _ => false));
            }
            else
            {
                results.Add(new Result($"{target.Name} · {sizeText}", $"{target.Hint} · Enter ile temizle", InfoPlugins.EmojiPrefix + target.Emoji, 900 - i, _ =>
                {
                    Run(host, [target]);
                    return true;
                }));
            }
        }

        return results;
    }

    private static void Run(IPluginHost? host, IReadOnlyList<CleanupTarget> targets) => _ = Task.Run(() =>
    {
        long freed = 0;
        var skipped = 0;
        foreach (var target in targets)
        {
            try
            {
                var outcome = target.Clean!();
                freed += outcome.FreedBytes;
                skipped += outcome.Skipped;
            }
            catch (Exception ex)
            {
                host?.Log(LogLevel.Warning, $"/temizle {target.Name}: {ex.Message}");
            }
        }

        host?.ShowMessage("Temizlik bitti", $"{ProcessKillPlugin.FormatBytes(freed)} yer açıldı."
            + (skipped > 0 ? $" Kullanımda olan {skipped} dosya atlandı." : ""));
    });

    private static long Safe(Func<long> measure)
    {
        try
        {
            return measure();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <summary>
/// "/başlangıç": Windows açılınca başlayan uygulamalar. Enter açar/kapatır (Görev Yöneticisi'ndeki gibi; kayıt silinmez).
/// Tüm kullanıcılar için kayıtlı olanlar yönetici izni ister; onlar için Windows ayarı açılır.
/// </summary>
public sealed class StartupPlugin(
    string keyword,
    Func<IReadOnlyList<StartupItem>> list,
    Func<StartupItem, bool, string?> setEnabled,
    Action openSettings) : IPlugin
{
    private IPluginHost? _host;

    public PluginMetadata Metadata { get; } = new($"lumorago.startup{(keyword == "/başlangıç" ? "" : ".ascii")}", "Başlangıç uygulamaları", keyword);

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var filter = FuzzyMatcher.Fold(query.Search.Trim());
        var items = list()
            .Select(item => (Item: item, Score: filter.Length == 0 ? 1 : FuzzyMatcher.Score(filter, item.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Item.Enabled)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var results = items.Select((x, i) =>
        {
            var item = x.Item;
            var state = item.Enabled ? "Açık · Enter ile kapat" : "Kapalı · Enter ile aç";
            return new Result(item.Name, $"{state}{(item.NeedsAdmin ? " (yönetici izni gerekir)" : "")} · {item.Location}", item.ExePath, 1000 - i, _ =>
            {
                if (setEnabled(item, !item.Enabled) is { } error)
                {
                    _host?.ShowMessage("Değiştirilemedi", error);
                    if (item.NeedsAdmin)
                    {
                        openSettings();
                        return true;
                    }
                }

                _host?.RefreshResults(Metadata.ActionKeyword!);
                return false;
            })
            {
                PreviewText = item.Command,
            };
        }).ToList();

        if (results.Count == 0)
        {
            results.Add(new Result(filter.Length == 0 ? "Açılışta başlayan uygulama yok" : "Eşleşen uygulama yok", null, null, 2, _ => false));
        }

        results.Add(new Result("Windows'un başlangıç uygulamaları ayarını aç", "Mağaza uygulamaları da orada", null, 1, _ =>
        {
            openSettings();
            return true;
        }));
        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
