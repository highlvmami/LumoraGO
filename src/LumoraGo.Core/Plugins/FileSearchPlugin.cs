using LumoraGo.Core.Files;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/f rapor": kullanıcı klasörlerindeki dosya ve klasörleri adıyla anında bulur.</summary>
public sealed class FileSearchPlugin : IPlugin
{
    public const string Keyword = "/f";
    public static readonly PluginMetadata Metadata = new("lumorago.files", "Dosya arama", Keyword);

    private const int MaxResults = 40;

    private readonly FileIndex _index;
    private readonly FileResults _files;

    public FileSearchPlugin(FileIndex index, Action<string> open, Action<string> reveal)
    {
        _index = index;
        _files = new FileResults(open, reveal);
    }

    public void Init(PluginContext context) => _files.Init(context.Host);

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (query.IsEmpty)
        {
            var hint = _index.IsReady
                ? $"{_index.Entries.Count:N0} dosya ve klasör hazır. Tüm diskte ve dosya içlerinde aramak için /ds"
                : "Dosya listesi hazırlanıyor…";
            return Task.FromResult<IReadOnlyList<Result>>([new Result("Dosya arama: \"/f ad\" yaz", hint, null, 1, _ => false)]);
        }

        var results = _index.Search(query.Search, MaxResults, ct)
            .Select(m => _files.Create(
                m.Entry.Path,
                m.Entry.IsDirectory,
                m.Entry.Name,
                m.Entry.IsDirectory ? $"Klasör · {m.Entry.Path}" : m.Entry.Path,
                m.Score))
            .ToList();

        if (results.Count == 0 && !_index.IsReady)
        {
            results.Add(new Result("Dosya listesi hazırlanıyor…", "Birkaç saniye içinde tekrar dene", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
