using System.Globalization;
using System.Text;
using System.Text.Json;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Snippets;

/// <param name="Keyword">";adres" gibi kısa ad; launcher'a yazınca bu metin çıkar.</param>
/// <param name="Category">Düzenleme ekranında gruplamak için, örn. "Kişisel", "İş".</param>
public sealed record Snippet(Guid Id, string Name, string Keyword, string Content, string Category, DateTime UpdatedAt, int UseCount = 0,
    DateTime? LastUsed = null);

/// <summary>Hazır metinler. Her değişiklikte JSON dosyasına yazılır.</summary>
public sealed class SnippetStore
{
    public const string Prefix = ";";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly string? _path;
    private readonly ILog _log;
    private readonly object _gate = new();
    private List<Snippet> _snippets = [];

    /// <param name="path">Dosya; null ise sadece bellekte tutulur (testler için).</param>
    public SnippetStore(string? path, ILog? log = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        if (path is not null && File.Exists(path))
        {
            try
            {
                _snippets = JsonSerializer.Deserialize<List<Snippet>>(File.ReadAllText(path)) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _log.Warn($"Hazır metinler okunamadı: {ex.Message}");
            }
        }
    }

    public event Action? Changed;

    /// <summary>Kategoriye, sonra ada göre sıralı.</summary>
    public IReadOnlyList<Snippet> All
    {
        get
        {
            lock (_gate)
            {
                return _snippets.OrderBy(s => s.Category, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }
    }

    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_gate)
            {
                return _snippets.Select(s => s.Category).Where(c => c.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }
    }

    public Snippet? Get(Guid id)
    {
        lock (_gate)
        {
            return _snippets.Find(s => s.Id == id);
        }
    }

    /// <summary>";Adres " → ";adres"; boşluklar tireye döner, baştaki ";" eklenir.</summary>
    public static string NormalizeKeyword(string keyword)
    {
        var text = keyword.Trim().TrimStart(';').Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            builder.Append(char.IsWhiteSpace(c) ? '-' : c);
        }

        return builder.Length == 0 ? "" : Prefix + builder;
    }

    /// <returns>Kaydedilemezse nedeni, yoksa null.</returns>
    public string? Validate(Snippet snippet)
    {
        if (snippet.Name.Trim().Length == 0)
        {
            return "Bir ad yaz.";
        }

        if (snippet.Content.Length == 0)
        {
            return "İçerik boş olamaz.";
        }

        var keyword = NormalizeKeyword(snippet.Keyword);
        if (keyword.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            var folded = FuzzyMatcher.Fold(keyword);
            return _snippets.FirstOrDefault(s => s.Id != snippet.Id && FuzzyMatcher.Fold(s.Keyword) == folded) is { } other
                ? $"\"{keyword}\" zaten \"{other.Name}\" için kullanılıyor."
                : null;
        }
    }

    /// <summary>Yeni ise ekler, varsa günceller. Geçersizse <see cref="InvalidOperationException"/>.</summary>
    public Snippet Save(Snippet snippet, DateTime? now = null)
    {
        if (Validate(snippet) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var saved = snippet with
        {
            Name = snippet.Name.Trim(),
            Keyword = NormalizeKeyword(snippet.Keyword),
            Category = snippet.Category.Trim(),
            UpdatedAt = now ?? DateTime.Now,
        };
        Change(list =>
        {
            var index = list.FindIndex(s => s.Id == saved.Id);
            if (index >= 0)
            {
                list[index] = saved with { UseCount = list[index].UseCount, LastUsed = list[index].LastUsed };
            }
            else
            {
                list.Add(saved);
            }
        });
        return saved;
    }

    public void Remove(Guid id) => Change(list => list.RemoveAll(s => s.Id == id));

    public void MarkUsed(Guid id, DateTime? now = null) => Change(list =>
    {
        var index = list.FindIndex(s => s.Id == id);
        if (index >= 0)
        {
            list[index] = list[index] with { UseCount = list[index].UseCount + 1, LastUsed = now ?? DateTime.Now };
        }
    });

    public string Export() => JsonSerializer.Serialize(All, Indented);

    /// <summary>Dışa aktarılmış JSON'u ekler; aynı kimlikteki metinleri günceller, çakışan kısa adları boşaltır.</summary>
    /// <returns>Eklenen/güncellenen sayısı.</returns>
    public int Import(string json)
    {
        var incoming = JsonSerializer.Deserialize<List<Snippet>>(json) ?? [];
        var count = 0;
        foreach (var snippet in incoming.Where(s => s.Name.Trim().Length > 0 && s.Content.Length > 0))
        {
            var item = snippet with { Category = snippet.Category ?? "", Keyword = snippet.Keyword ?? "" };
            if (Validate(item) is not null)
            {
                item = item with { Keyword = "" };
            }

            Save(item, item.UpdatedAt);
            count++;
        }

        return count;
    }

    private void Change(Action<List<Snippet>> change)
    {
        lock (_gate)
        {
            change(_snippets);
            if (_path is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(_snippets, Indented));
                    File.Move(temp, _path, overwrite: true);
                }
                catch (IOException ex)
                {
                    _log.Warn($"Hazır metinler kaydedilemedi: {ex.Message}");
                }
            }
        }

        Changed?.Invoke();
    }
}

/// <param name="Text">Yapıştırılacak metin.</param>
/// <param name="CharsAfterCursor">{imleç} varsa, yapıştırdıktan sonra imleci bu kadar sola al.</param>
public readonly record struct ExpandedSnippet(string Text, int CharsAfterCursor);

/// <summary>Hazır metindeki yer tutucuları doldurur: {tarih}, {saat}, {gün}, {ay}, {yıl}, {yarın}, {pano}, {imleç}.</summary>
public static class SnippetExpander
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly IReadOnlyList<(string Token, string Description)> Placeholders =
    [
        ("{tarih}", "Bugünün tarihi (14.10.2026)"),
        ("{saat}", "Şu anki saat (14:05)"),
        ("{gün}", "Haftanın günü (Çarşamba)"),
        ("{ay}", "Ayın adı (Ekim)"),
        ("{yıl}", "Yıl (2026)"),
        ("{yarın}", "Yarının tarihi"),
        ("{pano}", "Panodaki metin"),
        ("{imleç}", "Yapıştırınca imleç buraya gelir"),
    ];

    public static ExpandedSnippet Expand(string content, DateTime now, string? clipboard)
    {
        var text = content
            .Replace("{tarih}", now.ToString("dd.MM.yyyy", Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{saat}", now.ToString("HH:mm", Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{gün}", Turkish.TextInfo.ToTitleCase(now.ToString("dddd", Turkish)), StringComparison.OrdinalIgnoreCase)
            .Replace("{gun}", Turkish.TextInfo.ToTitleCase(now.ToString("dddd", Turkish)), StringComparison.OrdinalIgnoreCase)
            .Replace("{ay}", Turkish.TextInfo.ToTitleCase(now.ToString("MMMM", Turkish)), StringComparison.OrdinalIgnoreCase)
            .Replace("{yıl}", now.Year.ToString(Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{yil}", now.Year.ToString(Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{yarın}", now.AddDays(1).ToString("dd.MM.yyyy", Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{yarin}", now.AddDays(1).ToString("dd.MM.yyyy", Turkish), StringComparison.OrdinalIgnoreCase)
            .Replace("{pano}", clipboard ?? "", StringComparison.OrdinalIgnoreCase);

        foreach (var token in new[] { "{imleç}", "{imlec}" })
        {
            var index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                text = text.Remove(index, token.Length).Replace(token, "", StringComparison.OrdinalIgnoreCase);
                // Windows'ta satır sonu \r\n tek bir sol ok tuşuyla geçilir.
                return new(text, text[index..].Replace("\r\n", "\n").Length);
            }
        }

        return new(text, 0);
    }
}
