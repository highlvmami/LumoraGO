using System.Text.RegularExpressions;

namespace LumoraGo.Core;

/// <summary>
/// CHANGELOG.md'den bir sürümün notlarını okur. Biçim: her sürüm "## 0.2.0" (veya "## [0.2.0] - 2026-10-12") başlığı,
/// altında "- " ile başlayan maddeler. Release iş akışı da aynı kuralla notları çıkarır.
/// </summary>
public static class Changelog
{
    /// <returns>Sürümün maddeleri; sürüm başlığı yoksa boş liste.</returns>
    public static IReadOnlyList<string> GetNotes(string markdown, string version)
    {
        var heading = new Regex($@"^##\s+\[?v?{Regex.Escape(version)}\]?(\s|$)", RegexOptions.IgnoreCase);
        var notes = new List<string>();
        var inSection = false;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                {
                    break;
                }

                inSection = heading.IsMatch(line);
                continue;
            }

            var trimmed = line.Trim();
            if (inSection && (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)))
            {
                notes.Add(trimmed[2..].Trim());
            }
        }

        return notes;
    }
}
