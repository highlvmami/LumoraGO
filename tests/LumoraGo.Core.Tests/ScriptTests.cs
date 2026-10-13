using System.Diagnostics;
using LumoraGo.Core.Apps;
using LumoraGo.Core.Plugins;
using LumoraGo.Core.Scripts;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class ChainTextTests
{
    [Fact]
    public void Parses_chains_with_turkish_and_ascii_kinds()
    {
        var chains = ChainText.Parse("""
            # sabah rutini
            [çalışma]
            uygulama | Visual Studio Code
            Site | github.com
            ses | 30
            bekle | 1,5
            aç | C:\Projeler

            [akşam]
            yaz | iyi akşamlar
            bildirim | Kapatma vakti
            """, out var errors);

        Assert.Empty(errors);
        Assert.Equal(["çalışma", "akşam"], chains.Select(c => c.Name));
        Assert.Equal(
            [StepKind.App, StepKind.Site, StepKind.Volume, StepKind.Wait, StepKind.Open],
            chains[0].Steps.Select(s => s.Kind));
        Assert.Equal("github.com", chains[0].Steps[1].Argument);
        Assert.Equal(StepKind.Copy, chains[1].Steps[0].Kind);
    }

    [Fact]
    public void Reports_problems_with_line_numbers()
    {
        var chains = ChainText.Parse("""
            site | önce başlık yok
            [a]
            ses | 150
            bekle | sıfır
            uçur | bir yere
            site |
            site | ok.com
            [boş]
            [a]
            site | x.com
            """, out var errors);

        Assert.Contains(errors, e => e.StartsWith("1. satır") && e.Contains("[zincir adı]"));
        Assert.Contains(errors, e => e.StartsWith("3. satır") && e.Contains("0 ile 100"));
        Assert.Contains(errors, e => e.StartsWith("4. satır") && e.Contains("600"));
        Assert.Contains(errors, e => e.StartsWith("5. satır") && e.Contains("bilinmeyen"));
        Assert.Contains(errors, e => e.StartsWith("6. satır") && e.Contains("değer"));
        Assert.Contains(errors, e => e.Contains("\"boş\" zincirinde adım yok"));
        Assert.Contains(errors, e => e.Contains("\"a\" adlı iki zincir"));
        Assert.Single(chains);
    }

    [Fact]
    public void Empty_text_has_no_chains_and_no_errors()
    {
        Assert.Empty(ChainText.Parse("", out var errors));
        Assert.Empty(errors);
    }
}

public sealed class ChainRunnerTests
{
    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];

        public string? FailOn { get; init; }

        public ChainActions Actions => new(
            Open: p => Do("aç " + p),
            LaunchApp: a => Do("uygulama " + a),
            OpenUrl: u => Do("site " + u),
            RunScript: (s, _) => Task.FromResult(Do("betik " + s)),
            SetVolume: v => Do("ses " + v),
            Copy: t => Do("yaz " + t),
            Notify: (title, text) => Calls.Add($"bildirim {title}: {text}"),
            Delay: (t, _) =>
            {
                Calls.Add($"bekle {t.TotalSeconds}");
                return Task.CompletedTask;
            });

        private string? Do(string call)
        {
            Calls.Add(call);
            return call == FailOn ? "olmadı" : null;
        }
    }

    private static ActionChain Chain(string text) => ChainText.Parse(text, out _).Single();

    [Fact]
    public async Task Runs_every_step_in_order()
    {
        var recorder = new Recorder();
        var outcome = await ChainRunner.RunAsync(Chain("""
            [iş]
            uygulama | Code
            site | github.com
            ses | 40
            bekle | 0,5
            yaz | merhaba
            betik | yedekle D:\
            bildirim | hazır
            """), recorder.Actions, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal("7 adım tamamlandı", outcome.Message);
        Assert.Equal(
            ["uygulama Code", "site github.com", "ses 40", "bekle 0.5", "yaz merhaba", @"betik yedekle D:\", "bildirim iş: hazır"],
            recorder.Calls);
    }

    [Fact]
    public async Task Stops_at_the_first_failing_step()
    {
        var recorder = new Recorder { FailOn = "site github.com" };
        var outcome = await ChainRunner.RunAsync(Chain("""
            [iş]
            uygulama | Code
            site | github.com
            ses | 40
            """), recorder.Actions, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(2, outcome.FailedStep);
        Assert.Equal("2. adımda durdu (site github.com): olmadı", outcome.Message);
        Assert.DoesNotContain("ses 40", recorder.Calls);
    }

    [Fact]
    public async Task Exceptions_count_as_step_errors()
    {
        var actions = new Recorder().Actions with { OpenUrl = _ => throw new InvalidOperationException("tarayıcı yok") };
        var outcome = await ChainRunner.RunAsync(Chain("[a]\nsite | x.com"), actions, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.EndsWith("tarayıcı yok", outcome.Message);
    }
}

public sealed class ScriptCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-scripts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private void Touch(params string[] names)
    {
        Directory.CreateDirectory(_dir);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_dir, name), "");
        }
    }

    [Fact]
    public void Lists_only_supported_extensions_sorted()
    {
        Touch("yedekle.ps1", "temizle.bat", "derle.cmd", "rapor.py", "notlar.txt", "readme.md");
        var names = new ScriptCatalog(() => _dir).List().Select(s => s.Name);

        Assert.Equal(["derle", "rapor", "temizle", "yedekle"], names);
    }

    [Fact]
    public void Missing_folder_is_empty()
    {
        Assert.Empty(new ScriptCatalog(() => _dir).List());
    }

    [Fact]
    public void Ensure_folder_creates_sample_once()
    {
        var catalog = new ScriptCatalog(() => _dir);

        Assert.True(catalog.EnsureFolder());
        Assert.Equal(["merhaba"], catalog.List().Select(s => s.Name));

        File.Delete(Path.Combine(_dir, "merhaba.ps1"));
        Assert.False(catalog.EnsureFolder());
        Assert.Empty(catalog.List());
    }

    [Fact]
    public void Resolve_splits_name_and_arguments_preferring_longest_name()
    {
        Touch("yedekle.ps1", "yedekle hepsi.bat");
        var catalog = new ScriptCatalog(() => _dir);

        Assert.Equal(("yedekle", @"D:\Yedek"), Name(catalog.Resolve(@"yedekle D:\Yedek")));
        Assert.Equal(("yedekle hepsi", "şimdi"), Name(catalog.Resolve("YEDEKLE HEPSI şimdi")));
        Assert.Equal(("yedekle", ""), Name(catalog.Resolve(" yedekle ")));
        Assert.Null(catalog.Resolve("yedeklemek"));

        static (string, string)? Name((ScriptItem Script, string Arguments)? r) => r is { } x ? (x.Script.Name, x.Arguments) : null;
    }
}

public sealed class ScriptRunnerTests
{
    private static readonly ScriptItem Ps = new("yedekle", @"C:\s\yedekle.ps1");
    private static readonly ScriptItem Bat = new("temizle", @"C:\s\temizle.bat");
    private static readonly ScriptItem Py = new("rapor", @"C:\s\rapor.py");

    [Fact]
    public void Powershell_bypasses_policy_only_for_this_run()
    {
        var info = ScriptRunner.StartInfo(Ps, "D:\\", ScriptMode.Background);

        Assert.Equal("powershell.exe", info.FileName);
        Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\s\\yedekle.ps1\" D:\\", info.Arguments);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.CreateNoWindow);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void Visible_keeps_the_window_open()
    {
        Assert.Contains("-NoExit", ScriptRunner.StartInfo(Ps, "", ScriptMode.Visible).Arguments);
        Assert.Equal("/k \"\"C:\\s\\temizle.bat\" \"", ScriptRunner.StartInfo(Bat, "", ScriptMode.Visible).Arguments);
        Assert.Equal("cmd.exe", ScriptRunner.StartInfo(Py, "", ScriptMode.Visible).FileName);
    }

    [Fact]
    public void Batch_and_python_in_background()
    {
        var bat = ScriptRunner.StartInfo(Bat, "a b", ScriptMode.Background);
        Assert.Equal("cmd.exe", bat.FileName);
        Assert.Equal("/c \"\"C:\\s\\temizle.bat\" a b\"", bat.Arguments);

        var py = ScriptRunner.StartInfo(Py, "", ScriptMode.Background);
        Assert.Equal("py.exe", py.FileName);
        Assert.Equal("\"C:\\s\\rapor.py\"", py.Arguments);
    }

    [Fact]
    public void Admin_asks_for_elevation()
    {
        var info = ScriptRunner.StartInfo(Ps, "", ScriptMode.Admin);

        Assert.True(info.UseShellExecute);
        Assert.Equal("runas", info.Verb);
        Assert.False(info.RedirectStandardOutput);
    }

    [Fact]
    public async Task Run_passes_start_info_to_executor()
    {
        ProcessStartInfo? seen = null;
        var runner = new ScriptRunner((psi, _) =>
        {
            seen = psi;
            return Task.FromResult(new ScriptRun(0, "tamam"));
        });

        var run = await runner.RunAsync(Ps, "x", CancellationToken.None);

        Assert.Equal("tamam", run.Output);
        Assert.EndsWith(" x", seen!.Arguments);
    }
}

public sealed class ChainStepsTests
{
    private static AppEntry App(string name, string? target = null) =>
        new(name, name, "", name + ".lnk", target, AppKind.Shortcut);

    [Fact]
    public void Find_app_prefers_exact_name_then_exe_then_fuzzy()
    {
        var apps = new[]
        {
            App("Visual Studio 2022"),
            App("Visual Studio Code", @"C:\code\Code.exe"),
            App("Spotify"),
        };

        Assert.Equal("Visual Studio Code", ChainSteps.FindApp(apps, "visual studio code")!.Name);
        Assert.Equal("Visual Studio Code", ChainSteps.FindApp(apps, "code")!.Name);
        Assert.Equal("Spotify", ChainSteps.FindApp(apps, "spot")!.Name);
        Assert.Null(ChainSteps.FindApp(apps, "zzzz"));
        Assert.Null(ChainSteps.FindApp(apps, " "));
    }

    [Theory]
    [InlineData("github.com", "https://github.com")]
    [InlineData("http://x.com", "http://x.com")]
    [InlineData("mailto:a@b.com", "mailto:a@b.com")]
    [InlineData(" ms-settings:display ", "ms-settings:display")]
    public void Normalize_url(string input, string expected) =>
        Assert.Equal(expected, ChainSteps.NormalizeUrl(input));

    [Fact]
    public async Task Script_step_reports_missing_script_and_exit_codes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumorago-steps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "yedekle.ps1"), "");
            var catalog = new ScriptCatalog(() => dir);
            string? args = null;
            Task<ScriptRun> Run(ScriptItem s, string a, CancellationToken ct)
            {
                args = a;
                return Task.FromResult(a == "bozuk" ? new ScriptRun(2, "ilk\nson satır\n") : new ScriptRun(0, ""));
            }

            Assert.Null(await ChainSteps.RunScriptAsync(catalog, Run, "yedekle D:", CancellationToken.None));
            Assert.Equal("D:", args);
            Assert.Equal("çıkış kodu 2: son satır", await ChainSteps.RunScriptAsync(catalog, Run, "yedekle bozuk", CancellationToken.None));
            Assert.StartsWith("\"yok\" adında betik yok", await ChainSteps.RunScriptAsync(catalog, Run, "yok", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class ScriptsPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-splugin-" + Guid.NewGuid().ToString("N"));
    private readonly NullHost _host = new();
    private readonly List<string> _calls = [];

    public ScriptsPluginTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "yedekle.ps1"), "");
        File.WriteAllText(Path.Combine(_dir, "temizle.bat"), "");
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private ScriptsPlugin Plugin(string? keyword = null, string chains = "[çalışma]\nsite | github.com\nses | 20", int exitCode = 0)
    {
        var host = new ScriptHost(
            (s, a, _) =>
            {
                _calls.Add($"run {s.Name} [{a}]");
                return Task.FromResult(new ScriptRun(exitCode, exitCode == 0 ? "bitti" : "hata"));
            },
            (s, a, mode) => _calls.Add($"{mode} {s.Name} [{a}]"),
            () => new ChainActions(
                Open: _ => null,
                LaunchApp: _ => null,
                OpenUrl: u =>
                {
                    _calls.Add("site " + u);
                    return null;
                },
                RunScript: (_, _) => Task.FromResult<string?>(null),
                SetVolume: v =>
                {
                    _calls.Add("ses " + v);
                    return null;
                },
                Copy: _ => null,
                Notify: (_, _) => { }),
            f => _calls.Add("klasör " + f));
        var plugin = new ScriptsPlugin(new ScriptCatalog(() => _dir), () => ChainText.Parse(chains, out _), host, keyword);
        plugin.Init(new PluginContext(plugin.Metadata, _dir, _host));
        return plugin;
    }

    private static Task<IReadOnlyList<Result>> Ask(IPlugin plugin, string text, string? keyword = null) =>
        plugin.QueryAsync(new Query(keyword is null ? text : $"{keyword} {text}", keyword, text), CancellationToken.None);

    [Fact]
    public async Task Exact_name_with_arguments_ranks_high_and_runs_in_background()
    {
        var plugin = Plugin();
        var top = (await Ask(plugin, @"yedekle D:\Yedek")).First();

        Assert.Equal(@"yedekle D:\Yedek", top.Title);
        Assert.Equal(ScriptsPlugin.Exact, top.Score);
        Assert.EndsWith("yedekle.ps1", top.PreviewPath);

        Assert.True(top.Action(new ActionContext(ActionModifiers.None)));
        await Wait.Until(() => _host.Messages.Count > 0);
        Assert.Equal([@"run yedekle [D:\Yedek]"], _calls);
        Assert.Equal(("yedekle bitti", "bitti"), _host.Messages[0]);
    }

    [Fact]
    public async Task Failing_script_reports_exit_code()
    {
        var plugin = Plugin(exitCode: 3);
        (await Ask(plugin, "temizle")).First().Action(new ActionContext(ActionModifiers.None));

        await Wait.Until(() => _host.Messages.Count > 0);
        Assert.Equal(("temizle hata verdi (kod 3)", "hata"), _host.Messages[0]);
    }

    [Fact]
    public async Task Ctrl_opens_terminal_and_shift_runs_as_admin()
    {
        var plugin = Plugin();
        var row = (await Ask(plugin, "temizle a")).First();

        row.Action(new ActionContext(ActionModifiers.Ctrl));
        row.Action(new ActionContext(ActionModifiers.Shift));

        Assert.Equal(["Visible temizle [a]", "Admin temizle [a]"], _calls);
    }

    [Fact]
    public async Task Chains_are_found_by_name_and_run_steps()
    {
        var plugin = Plugin();
        var row = (await Ask(plugin, "calisma")).First();

        Assert.Equal("çalışma", row.Title);
        Assert.Contains("site github.com → ses %20", row.Subtitle);

        row.Action(new ActionContext(ActionModifiers.None));
        await Wait.Until(() => _host.Messages.Count > 0);
        Assert.Equal(["site github.com", "ses 20"], _calls);
        Assert.Equal(("çalışma", "2 adım tamamlandı"), _host.Messages[0]);
    }

    [Fact]
    public async Task Global_search_stays_quiet_for_short_or_unrelated_text()
    {
        var plugin = Plugin();

        Assert.Empty(await Ask(plugin, "y"));
        Assert.Empty(await Ask(plugin, "spotify"));
    }

    [Fact]
    public async Task Typing_betik_offers_the_folder()
    {
        var plugin = Plugin();
        var row = (await Ask(plugin, "betik")).Single();

        Assert.Equal("Betik klasörünü aç", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["klasör " + _dir], _calls);
    }

    [Fact]
    public async Task Keyword_lists_everything_when_empty()
    {
        var plugin = Plugin(ScriptsPlugin.RunKeyword);
        var titles = (await Ask(plugin, "", ScriptsPlugin.RunKeyword)).Select(r => r.Title).ToList();

        Assert.Equal("Betik klasörünü aç", titles[0]);
        Assert.Contains("yedekle", titles);
        Assert.Contains("temizle", titles);
        Assert.Contains("çalışma", titles);
        Assert.Equal("lumorago.scripts.run", plugin.Metadata.Id);
        Assert.Equal("lumorago.scripts.run.calistir", Plugin("/çalıştır").Metadata.Id);
    }
}
