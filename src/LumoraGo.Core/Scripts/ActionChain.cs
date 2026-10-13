using LumoraGo.Core.Search;

namespace LumoraGo.Core.Scripts;

public enum StepKind
{
    Open,
    App,
    Site,
    Script,
    Volume,
    Wait,
    Copy,
    Notify,
}

public sealed record ChainStep(StepKind Kind, string Argument);

public sealed record ActionChain(string Name, IReadOnlyList<ChainStep> Steps);

/// <summary>
/// Ayarlar'daki eylem zinciri metni. Her zincir "[ad]" satırıyla başlar, altında her satıra bir adım: "tür | değer".
/// Türler: aç, uygulama, site, betik, ses, bekle, yaz, bildirim. "#" ile başlayan ve boş satırlar atlanır.
/// </summary>
public static class ChainText
{
    private static readonly Dictionary<string, StepKind> Kinds = new()
    {
        ["ac"] = StepKind.Open,
        ["uygulama"] = StepKind.App,
        ["site"] = StepKind.Site,
        ["betik"] = StepKind.Script,
        ["ses"] = StepKind.Volume,
        ["bekle"] = StepKind.Wait,
        ["yaz"] = StepKind.Copy,
        ["bildirim"] = StepKind.Notify,
    };

    public static IReadOnlyList<ActionChain> Parse(string text, out IReadOnlyList<string> errors)
    {
        var chains = new List<ActionChain>();
        var problems = new List<string>();
        string? name = null;
        var steps = new List<ChainStep>();

        void Close()
        {
            if (name is null)
            {
                return;
            }

            if (steps.Count == 0)
            {
                problems.Add($"\"{name}\" zincirinde adım yok.");
            }
            else if (chains.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"\"{name}\" adlı iki zincir var.");
            }
            else
            {
                chains.Add(new ActionChain(name, steps));
            }
        }

        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Close();
                name = line[1..^1].Trim();
                steps = [];
                if (name.Length == 0)
                {
                    problems.Add($"{i + 1}. satır: zincir adı boş.");
                    name = null;
                }

                continue;
            }

            if (name is null)
            {
                problems.Add($"{i + 1}. satır: önce \"[zincir adı]\" satırı yazılmalı.");
                continue;
            }

            var bar = line.IndexOf('|');
            var kindText = bar < 0 ? line : line[..bar].Trim();
            var argument = bar < 0 ? "" : line[(bar + 1)..].Trim();
            if (!Kinds.TryGetValue(FuzzyMatcher.Fold(kindText), out var kind))
            {
                problems.Add($"{i + 1}. satır: \"{kindText}\" bilinmeyen adım. Olabilecekler: aç, uygulama, site, betik, ses, bekle, yaz, bildirim.");
                continue;
            }

            var bad = kind switch
            {
                StepKind.Volume => !int.TryParse(argument, out var v) || v is < 0 or > 100 ? "ses 0 ile 100 arasında bir sayı olmalı" : null,
                StepKind.Wait => !double.TryParse(argument.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) || s is <= 0 or > 600
                    ? "bekle 0 ile 600 saniye arasında olmalı" : null,
                _ => argument.Length == 0 ? "\"|\" işaretinden sonra değer yazılmalı" : null,
            };
            if (bad is not null)
            {
                problems.Add($"{i + 1}. satır: {bad}.");
                continue;
            }

            steps.Add(new ChainStep(kind, argument));
        }

        Close();
        errors = problems;
        return chains;
    }
}

/// <summary>Zincir adımlarını gerçekte yapan işlemler; her biri hata mesajı ya da başarıda null döndürür.</summary>
public sealed record ChainActions(
    Func<string, string?> Open,
    Func<string, string?> LaunchApp,
    Func<string, string?> OpenUrl,
    Func<string, CancellationToken, Task<string?>> RunScript,
    Func<int, string?> SetVolume,
    Func<string, string?> Copy,
    Action<string, string> Notify,
    Func<TimeSpan, CancellationToken, Task>? Delay = null);

public sealed record ChainOutcome(bool Ok, int? FailedStep, string Message);

public static class ChainRunner
{
    public static async Task<ChainOutcome> RunAsync(ActionChain chain, ChainActions actions, CancellationToken ct)
    {
        var delay = actions.Delay ?? Task.Delay;
        for (var i = 0; i < chain.Steps.Count; i++)
        {
            var step = chain.Steps[i];
            string? error;
            try
            {
                error = step.Kind switch
                {
                    StepKind.Open => actions.Open(step.Argument),
                    StepKind.App => actions.LaunchApp(step.Argument),
                    StepKind.Site => actions.OpenUrl(step.Argument),
                    StepKind.Script => await actions.RunScript(step.Argument, ct),
                    StepKind.Volume => actions.SetVolume(int.Parse(step.Argument, System.Globalization.CultureInfo.InvariantCulture)),
                    StepKind.Copy => actions.Copy(step.Argument),
                    StepKind.Notify => Notify(actions, chain, step.Argument),
                    StepKind.Wait => await Wait(delay, step.Argument, ct),
                    _ => null,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error is not null)
            {
                return new ChainOutcome(false, i + 1, $"{i + 1}. adımda durdu ({Describe(step)}): {error}");
            }
        }

        return new ChainOutcome(true, null, $"{chain.Steps.Count} adım tamamlandı");
    }

    private static string? Notify(ChainActions actions, ActionChain chain, string text)
    {
        actions.Notify(chain.Name, text);
        return null;
    }

    private static async Task<string?> Wait(Func<TimeSpan, CancellationToken, Task> delay, string argument, CancellationToken ct)
    {
        var seconds = double.Parse(argument.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        await delay(TimeSpan.FromSeconds(seconds), ct);
        return null;
    }

    public static string Describe(ChainStep step) => step.Kind switch
    {
        StepKind.Open => $"aç {step.Argument}",
        StepKind.App => $"uygulama {step.Argument}",
        StepKind.Site => $"site {step.Argument}",
        StepKind.Script => $"betik {step.Argument}",
        StepKind.Volume => $"ses %{step.Argument}",
        StepKind.Wait => $"bekle {step.Argument} sn",
        StepKind.Copy => "panoya yaz",
        StepKind.Notify => "bildirim",
        _ => step.Kind.ToString(),
    };
}
