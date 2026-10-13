using LumoraGo.Core.Apps;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Kurulu uygulamaları bulanık aramayla bulur ve açar. Enter açar, Ctrl+Enter klasörünü, Shift+Enter yönetici olarak açar.</summary>
public sealed class AppsPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.apps", "Uygulamalar");

    private const int MaxResults = 30;
    private const int ExeNameWeightPercent = 80;

    private readonly IAppSource _source;
    private readonly IAppLauncher _launcher;
    private readonly UsageStore _usage;
    private IPluginHost? _host;

    public AppsPlugin(IAppSource source, IAppLauncher launcher, UsageStore usage)
    {
        _source = source;
        _launcher = launcher;
        _usage = usage;
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(query.Search);
        var matches = new List<(AppEntry App, int Score)>();

        foreach (var app in _source.Apps)
        {
            var score = FuzzyMatcher.Score(folded, app.FoldedName);
            if (app.FoldedExeName is { } exe)
            {
                score = Math.Max(score, FuzzyMatcher.Score(folded, exe) * ExeNameWeightPercent / 100);
            }

            if (score > 0)
            {
                matches.Add((app, score + _usage.Boost(app.Id)));
            }
        }

        var results = matches
            .OrderByDescending(m => m.Score)
            .Take(MaxResults)
            .Select(m => new Result(m.App.Name, m.App.Subtitle, m.App.LaunchPath, m.Score, ctx => Launch(m.App, ctx), Menu(m.App))
            {
                PreviewPath = m.App.TargetPath is { Length: > 0 } target ? target : null,

                // Başlat menüsü kısayolu bırakıldığı yere kopyalanır (masaüstüne kısayol koymanın kolay yolu).
                DragData = m.App.Kind == AppKind.Shortcut ? DragPayload.File(m.App.LaunchPath) : null,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private IReadOnlyList<ContextAction> Menu(AppEntry app)
    {
        var menu = new List<ContextAction>
        {
            new("Aç", _ => Launch(app, new ActionContext(ActionModifiers.None))),
            new("Dosya konumunu aç", _ => Launch(app, new ActionContext(ActionModifiers.Ctrl))),
        };

        if (app.Kind == AppKind.Shortcut)
        {
            menu.Insert(1, new("Yönetici olarak çalıştır", _ => Launch(app, new ActionContext(ActionModifiers.Shift))));
        }

        if (app.TargetPath is { Length: > 0 } target)
        {
            menu.Add(new("Yolu kopyala", _ =>
            {
                _host?.CopyToClipboard(target);
                return true;
            }));
        }

        return menu;
    }

    private bool Launch(AppEntry app, ActionContext context)
    {
        var mode = LaunchMode.Normal;
        if (context.Modifiers.HasFlag(ActionModifiers.Ctrl)) mode |= LaunchMode.OpenFolder;
        if (context.Modifiers.HasFlag(ActionModifiers.Shift)) mode |= LaunchMode.AsAdmin;

        try
        {
            _launcher.Launch(app, mode);
        }
        catch (Exception ex)
        {
            _host?.Log(LogLevel.Error, $"Uygulama açılamadı: {app.Name}: {ex.Message}");
            _host?.ShowMessage("Açılamadı", $"{app.Name} açılamadı: {ex.Message}");
            return false;
        }

        if (!mode.HasFlag(LaunchMode.OpenFolder))
        {
            _usage.Record(app.Id);
        }

        return true;
    }
}
