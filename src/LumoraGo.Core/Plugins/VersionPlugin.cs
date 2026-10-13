using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <param name="Version">Çalışan sürüm, örn. "0.2.0".</param>
/// <param name="Notes">Bu sürümün sürüm notları.</param>
/// <param name="UpdateStatus">Otomatik güncellemenin durumu ("Güncel", "0.3.0 indiriliyor"...).</param>
public sealed record VersionInfo(string Version, IReadOnlyList<string> Notes, string UpdateStatus);

/// <summary>"/v" yazılınca çalışan sürümü, güncelleme durumunu ve sürüm notlarını gösterir.</summary>
public sealed class VersionPlugin : IPlugin
{
    public const string Keyword = "/v";
    public static readonly PluginMetadata Metadata = new("lumorago.version", "Sürüm bilgisi", Keyword);

    private readonly Func<VersionInfo> _source;

    /// <param name="source">Her sorguda çağrılır; güncelleme durumu değiştikçe güncel bilgiyi verir.</param>
    public VersionPlugin(Func<VersionInfo> source)
    {
        _source = source;
    }

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var info = _source();
        var notes = info.Notes.Count > 0 ? info.Notes : ["Bu sürüm için sürüm notu yok"];

        // Puanlar azalan: önce sürüm satırı, sonra notlar yazıldığı sırayla.
        var results = new List<Result>
        {
            new($"LumoraGo {info.Version}", info.UpdateStatus, null, notes.Count + 1, _ => false),
        };
        results.AddRange(notes.Select((note, i) =>
            new Result(note, $"{info.Version} sürüm notu", null, notes.Count - i, _ => false)));

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
