using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/kilit" gibi bir tetik kelimesine tek bir eylem bağlar; Enter eylemi çalıştırır.</summary>
public sealed class ShortcutPlugin : IPlugin
{
    private readonly string _title;
    private readonly string _subtitle;
    private readonly Action _action;

    /// <param name="keyword">Tetik kelimesi, örn. "/kilit".</param>
    /// <param name="title">Sonuç satırında görünen ad; "/liste"de de bu ad kullanılır.</param>
    /// <param name="subtitle">Ne yaptığının kısa açıklaması.</param>
    /// <param name="action">Seçilince çalışır; ardından pencere gizlenir.</param>
    public ShortcutPlugin(string keyword, string title, string subtitle, Action action)
    {
        Metadata = new PluginMetadata($"lumorago.shortcut.{keyword.TrimStart('/')}", title, keyword);
        _title = title;
        _subtitle = subtitle;
        _action = action;
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var result = new Result(_title, _subtitle, null, 100, _ =>
        {
            _action();
            return true;
        });
        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }
}
