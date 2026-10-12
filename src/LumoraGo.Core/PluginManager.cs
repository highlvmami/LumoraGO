using LumoraGo.Plugin;

namespace LumoraGo.Core;

public sealed record PluginEntry(PluginMetadata Metadata, IPlugin Plugin);

/// <summary>Eklentileri başlatır ve çalışır durumda olanları tutar. Init'te çöken eklenti devre dışı kalır, launcher çökmez.</summary>
public sealed class PluginManager
{
    private readonly List<PluginEntry> _active = [];
    private readonly ILog _log;

    public PluginManager(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
    }

    public IReadOnlyList<PluginEntry> Plugins => _active;

    public IReadOnlySet<string> ActionKeywords =>
        _active.Select(p => p.Metadata.ActionKeyword)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void Initialize(IEnumerable<PluginEntry> plugins, IPluginHost host, string dataRoot)
    {
        foreach (var entry in plugins)
        {
            if (_active.Any(p => p.Metadata.Id == entry.Metadata.Id))
            {
                _log.Warn($"Aynı kimlikte ikinci eklenti atlandı: {entry.Metadata.Id}");
                continue;
            }

            try
            {
                var dataDir = Path.Combine(dataRoot, entry.Metadata.Id);
                entry.Plugin.Init(new PluginContext(entry.Metadata, dataDir, host));
                _active.Add(entry);
            }
            catch (Exception ex)
            {
                _log.Error($"Eklenti başlatılamadı, devre dışı: {entry.Metadata.Id}: {ex}");
            }
        }
    }
}
