using LumoraGo.Core.Apps;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Scripts;

/// <summary>Zincir adımlarının ortak (Windows'a bağlı olmayan) kısımları.</summary>
public static class ChainSteps
{
    /// <summary>Adı en iyi eşleşen kurulu uygulama; tam ad önce gelir.</summary>
    public static AppEntry? FindApp(IEnumerable<AppEntry> apps, string name)
    {
        var folded = FuzzyMatcher.Fold(name.Trim());
        if (folded.Length == 0)
        {
            return null;
        }

        AppEntry? best = null;
        var bestScore = 0;
        foreach (var app in apps)
        {
            var score = app.FoldedName.Folded == folded ? int.MaxValue : FuzzyMatcher.Score(folded, app.FoldedName);
            if (app.FoldedExeName is { } exe && exe.Folded == folded)
            {
                score = int.MaxValue;
            }

            if (score > bestScore)
            {
                best = app;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Şeması olmayan adrese https ekler ("github.com" → "https://github.com").</summary>
    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        return url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)
            ? url
            : "https://" + url;
    }

    /// <summary>Betik adımı: betiği bulur, çalıştırır; sıfır dışı çıkış kodu hata sayılır.</summary>
    public static async Task<string?> RunScriptAsync(
        ScriptCatalog catalog, Func<ScriptItem, string, CancellationToken, Task<ScriptRun>> run, string text, CancellationToken ct)
    {
        if (catalog.Resolve(text) is not { } found)
        {
            return $"\"{text}\" adında betik yok ({catalog.Folder})";
        }

        var result = await run(found.Script, found.Arguments, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return null;
        }

        var last = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return last is null ? $"çıkış kodu {result.ExitCode}" : $"çıkış kodu {result.ExitCode}: {last}";
    }
}
