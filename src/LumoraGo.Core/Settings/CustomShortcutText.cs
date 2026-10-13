using LumoraGo.Core.Plugins;

namespace LumoraGo.Core.Settings;

/// <summary>
/// Ayarlar penceresindeki kısayol kutusunun metin biçimi: her satır "kelime | hedef" ya da "kelime | ad | hedef".
/// "#" ile başlayan ve boş satırlar atlanır.
/// </summary>
public static class CustomShortcutText
{
    public static IReadOnlyList<CustomShortcut> Parse(string text, out IReadOnlyList<string> errors)
    {
        var shortcuts = new List<CustomShortcut>();
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r", "").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length is < 2 or > 3 || parts[0].Length == 0 || parts[^1].Length == 0 || parts[0].Contains(' '))
            {
                problems.Add($"{i + 1}. satır anlaşılmadı: \"kelime | hedef\" veya \"kelime | ad | hedef\" yazılmalı.");
                continue;
            }

            if (!seen.Add(parts[0]))
            {
                problems.Add($"{i + 1}. satır: \"{parts[0]}\" iki kez yazılmış.");
                continue;
            }

            shortcuts.Add(new CustomShortcut(parts[0], parts.Length == 3 ? parts[1] : "", parts[^1]));
        }

        errors = problems;
        return shortcuts;
    }

    public static string Format(IEnumerable<CustomShortcut> shortcuts) =>
        string.Join(Environment.NewLine, shortcuts.Select(s =>
            s.Name.Length > 0 ? $"{s.Keyword} | {s.Name} | {s.Target}" : $"{s.Keyword} | {s.Target}"));
}
