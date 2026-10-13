using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <param name="Path">Açılacak yol (Windows'un "Son kullanılanlar" kısayolu olabilir).</param>
public sealed record RecentItem(string Name, string Path, DateTime UsedAt);

/// <summary>"/son": Windows'un son açılan dosya ve klasörleri, en yenisi başta; yazınca süzülür.</summary>
public sealed class RecentFilesPlugin(Func<IReadOnlyList<RecentItem>> recent, Action<string> open) : IPlugin
{
    public const string Keyword = "/son";
    public static readonly PluginMetadata Metadata = new("lumorago.recent", "Son açılan dosyalar", Keyword);

    private const int MaxResults = 40;

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        var results = recent()
            .OrderByDescending(r => r.UsedAt)
            .Where(r => folded.Length == 0 || FuzzyMatcher.Score(folded, r.Name) > 0)
            .Take(MaxResults)
            .Select((r, i) => new Result(r.Name, $"{Ago(r.UsedAt, DateTime.Now)} · Enter ile aç", null, MaxResults - i, _ =>
            {
                open(r.Path);
                return true;
            }))
            .ToList();

        if (results.Count == 0)
        {
            results.Add(new Result(folded.Length == 0 ? "Son açılan dosya yok" : "Eşleşen dosya yok", null, null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static string Ago(DateTime when, DateTime now)
    {
        var span = now - when;
        return span switch
        {
            { TotalMinutes: < 1 } => "az önce",
            { TotalHours: < 1 } => $"{(int)span.TotalMinutes} dk önce",
            { TotalDays: < 1 } => $"{(int)span.TotalHours} saat önce",
            { TotalDays: < 2 } => "dün",
            { TotalDays: < 30 } => $"{(int)span.TotalDays} gün önce",
            _ => when.ToString("d MMM yyyy", global::System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
        };
    }
}
