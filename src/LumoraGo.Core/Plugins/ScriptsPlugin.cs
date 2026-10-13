using LumoraGo.Core.Scripts;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Betik ve zinciri çalıştırmanın yolları; testte sahteleri verilir.</summary>
/// <param name="Run">Arka planda çalıştırır, bitince sonucu döner.</param>
/// <param name="Launch">Terminalde ya da yönetici olarak görünür başlatır.</param>
/// <param name="Chain">Zincir adımlarını yapan işlemler.</param>
/// <param name="OpenFolder">Betik klasörünü Gezgin'de açar.</param>
public sealed record ScriptHost(
    Func<ScriptItem, string, CancellationToken, Task<ScriptRun>> Run,
    Action<ScriptItem, string, ScriptMode> Launch,
    Func<ChainActions> Chain,
    Action<string> OpenFolder);

/// <summary>
/// Betik klasöründeki dosyaları ve Ayarlar'da tanımlanan eylem zincirlerini adıyla çalıştırır. Hem genel aramada
/// ("yedekle") hem "/calistir" ile çalışır; adından sonra yazılanlar betiğe argüman olarak gider.
/// Enter arka planda çalıştırıp bitince bildirir, Ctrl+Enter terminalde, Shift+Enter yönetici olarak açar.
/// </summary>
public sealed class ScriptsPlugin : IPlugin
{
    public static readonly PluginMetadata GlobalMetadata = new("lumorago.scripts", "Betikler");

    public const string RunKeyword = "/calistir";
    public const int Exact = 4000;
    private const int MaxResults = 20;

    private readonly ScriptCatalog _catalog;
    private readonly Func<IReadOnlyList<ActionChain>> _chains;
    private readonly ScriptHost _run;
    private IPluginHost? _host;

    /// <param name="keyword">Verilirse ("/calistir") yalnızca bu kelimeyle çalışır ve boş yazınca hepsini listeler.</param>
    public ScriptsPlugin(ScriptCatalog catalog, Func<IReadOnlyList<ActionChain>> chains, ScriptHost run, string? keyword = null)
    {
        _catalog = catalog;
        _chains = chains;
        _run = run;
        Metadata = keyword is null
            ? GlobalMetadata
            : new PluginMetadata(keyword == RunKeyword ? "lumorago.scripts.run" : "lumorago.scripts.run." + FuzzyMatcher.Fold(keyword).Trim('/'),
                "Betik çalıştır", keyword);
    }

    public PluginMetadata Metadata { get; }

    private bool IsKeyword => Metadata.ActionKeyword is not null;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim();
        var folded = FuzzyMatcher.Fold(text);
        var results = new List<Result>();

        if (IsKeyword || folded is "betik" or "betikler")
        {
            results.Add(FolderRow(IsKeyword && text.Length > 0 ? 1 : Exact));
        }

        if (!IsKeyword && text.Length < 2)
        {
            return Task.FromResult<IReadOnlyList<Result>>(results);
        }

        foreach (var script in _catalog.List())
        {
            if (Match(script.Name, text, folded) is { } m)
            {
                results.Add(ScriptRow(script, m.Arguments, m.Score));
            }
        }

        foreach (var chain in _chains())
        {
            if (Match(chain.Name, text, folded) is { } m)
            {
                results.Add(ChainRow(chain, m.Score));
            }
        }

        return Task.FromResult<IReadOnlyList<Result>>(results
            .OrderByDescending(r => r.Score)
            .Take(MaxResults)
            .ToList());
    }

    /// <summary>Ad tam yazıldıysa (arkasından argüman gelebilir) yüksek puan, değilse bulanık eşleşme.</summary>
    private (int Score, string Arguments)? Match(string name, string text, string folded)
    {
        if (text.Length == 0)
        {
            return (100, "");
        }

        var foldedName = FuzzyMatcher.Fold(name);
        if (folded == foldedName)
        {
            return (Exact, "");
        }

        if (folded.StartsWith(foldedName + " ", StringComparison.Ordinal))
        {
            return (Exact, text[(name.Length + 1)..].Trim());
        }

        var score = FuzzyMatcher.Score(folded, foldedName);
        if (score <= 0)
        {
            return null;
        }

        // Genel aramada betikler uygulamaların önüne geçmesin; tam ad yazılınca zaten üstte.
        return (IsKeyword ? score : score / 2, "");
    }

    private Result FolderRow(int score) =>
        new("Betik klasörünü aç", $"{_catalog.Folder} · .ps1, .bat, .cmd, .py dosyalarını buraya koy", null, score, _ =>
        {
            _catalog.EnsureFolder();
            _run.OpenFolder(_catalog.Folder);
            return true;
        })
        {
        };

    private Result ScriptRow(ScriptItem script, string arguments, int score)
    {
        var title = arguments.Length > 0 ? $"{script.Name} {arguments}" : script.Name;
        return new Result(title, $"Betik ({script.Extension}) · Enter arka planda · Ctrl+Enter terminalde · Shift+Enter yönetici", null, score,
            ctx => RunScript(script, arguments, ctx.Modifiers),
            [
                new ContextAction("Terminalde çalıştır", _ => RunScript(script, arguments, ActionModifiers.Ctrl)),
                new ContextAction("Yönetici olarak çalıştır", _ => RunScript(script, arguments, ActionModifiers.Shift)),
                new ContextAction("Betik klasörünü aç", _ =>
                {
                    _run.OpenFolder(_catalog.Folder);
                    return true;
                }),
            ])
        {
            AutoComplete = script.Name + " ",
            PreviewPath = script.Path,
        };
    }

    private Result ChainRow(ActionChain chain, int score) =>
        new(chain.Name, $"Eylem zinciri · {string.Join(" → ", chain.Steps.Select(ChainRunner.Describe))}", null, score, ctx =>
        {
            _ = RunChain(chain);
            return true;
        })
        {
            PreviewText = string.Join(Environment.NewLine, chain.Steps.Select((s, i) => $"{i + 1}. {ChainRunner.Describe(s)}")),
        };

    private bool RunScript(ScriptItem script, string arguments, ActionModifiers modifiers)
    {
        var mode = modifiers.HasFlag(ActionModifiers.Shift) ? ScriptMode.Admin
            : modifiers.HasFlag(ActionModifiers.Ctrl) ? ScriptMode.Visible
            : ScriptMode.Background;
        if (mode != ScriptMode.Background)
        {
            try
            {
                _run.Launch(script, arguments, mode);
            }
            catch (Exception ex)
            {
                // Yönetici onayını reddetmek de buraya düşer.
                _host?.ShowMessage(script.Name, $"Başlatılamadı: {ex.Message}");
            }

            return true;
        }

        _ = RunInBackground(script, arguments);
        return true;
    }

    /// <summary>Test edilebilsin diye görev döner; eylem beklemez.</summary>
    internal async Task RunInBackground(ScriptItem script, string arguments)
    {
        try
        {
            var run = await _run.Run(script, arguments, CancellationToken.None).ConfigureAwait(false);
            var output = run.Output.Trim();
            var title = run.ExitCode == 0 ? $"{script.Name} bitti" : $"{script.Name} hata verdi (kod {run.ExitCode})";
            _host?.ShowMessage(title, output.Length > 0 ? output : "Çıktı yok");
        }
        catch (Exception ex)
        {
            _host?.ShowMessage(script.Name, $"Çalıştırılamadı: {ex.Message}");
            _host?.Log(LogLevel.Warning, $"Betik {script.Path}: {ex}");
        }
    }

    internal async Task RunChain(ActionChain chain)
    {
        try
        {
            var outcome = await ChainRunner.RunAsync(chain, _run.Chain(), CancellationToken.None).ConfigureAwait(false);
            _host?.ShowMessage(chain.Name, outcome.Message);
        }
        catch (Exception ex)
        {
            _host?.ShowMessage(chain.Name, $"Zincir durdu: {ex.Message}");
        }
    }
}
