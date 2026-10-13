using System.Text.Json;

namespace LumoraGo.Core.Browser;

/// <param name="Folder">Yer iminin klasör yolu, örn. "Yer işaretleri çubuğu / İş".</param>
public sealed record Bookmark(string Title, string Url, string Folder, string Browser);

/// <summary>
/// Chromium tabanlı tarayıcıların (Chrome, Edge, Brave...) "Bookmarks" JSON dosyalarını okur.
/// Dosya değişmedikçe tekrar okumaz.
/// </summary>
public sealed class BookmarkSource(Func<IEnumerable<(string Path, string Browser)>> files, ILog? log = null)
{
    private readonly ILog _log = log ?? NullLog.Instance;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Stamp, IReadOnlyList<Bookmark> Items)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Bookmark> All()
    {
        var all = new List<Bookmark>();
        foreach (var (path, browser) in files())
        {
            all.AddRange(Read(path, browser));
        }

        // Aynı adres birden fazla tarayıcıda/profilde olabilir; bir kez göster.
        return all.DistinctBy(b => b.Url, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<Bookmark> Parse(string json, string browser)
    {
        var list = new List<Bookmark>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        foreach (var root in roots.EnumerateObject())
        {
            if (root.Value.ValueKind == JsonValueKind.Object)
            {
                Walk(root.Value, "", browser, list);
            }
        }

        return list;
    }

    private static void Walk(JsonElement node, string folder, string browser, List<Bookmark> list)
    {
        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        var name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (type == "url" && node.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } address)
        {
            list.Add(new Bookmark(name.Length > 0 ? name : address, address, folder, browser));
            return;
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var path = name.Length == 0 ? folder : folder.Length == 0 ? name : $"{folder} / {name}";
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, path, browser, list);
            }
        }
    }

    private IReadOnlyList<Bookmark> Read(string path, string browser)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            lock (_gate)
            {
                if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                {
                    return cached.Items;
                }
            }

            // Tarayıcı dosyayı yazarken açık tutabilir; paylaşımlı okuma.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var items = Parse(reader.ReadToEnd(), browser);
            lock (_gate)
            {
                _cache[path] = (stamp, items);
            }

            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"Yer imleri okunamadı: {path}: {ex.Message}");
            return [];
        }
    }
}
