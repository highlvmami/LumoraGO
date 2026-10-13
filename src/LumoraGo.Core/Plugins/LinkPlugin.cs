using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/git" gibi bir tetik kelimesiyle sabit bir adresi varsayılan tarayıcıda açar.</summary>
public sealed class LinkPlugin : IPlugin
{
    private readonly string _siteName;
    private readonly Action<string> _openUrl;

    public LinkPlugin(PluginMetadata metadata, string siteName, string url, Action<string> openUrl)
    {
        Metadata = metadata;
        _siteName = siteName;
        Url = url;
        _openUrl = openUrl;
    }

    public PluginMetadata Metadata { get; }

    public string Url { get; }

    public static LinkPlugin GitHub(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.github", "GitHub profilini aç", "/git"),
        "GitHub",
        "https://github.com/highlvmami",
        openUrl);

    /// <summary>"/linkedin" ve yaygın yazımı "/linkedln" için ayrı kayıtlar; ikisi de aynı sayfayı açar.</summary>
    public static LinkPlugin LinkedIn(Action<string> openUrl, bool misspelled = false) => new(
        new PluginMetadata(
            misspelled ? "lumorago.linkedln" : "lumorago.linkedin",
            "LinkedIn ana sayfasını aç",
            misspelled ? "/linkedln" : "/linkedin"),
        "LinkedIn",
        "https://www.linkedin.com/feed/",
        openUrl);

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var result = new Result($"{_siteName}'ı aç", Url, null, 100, _ =>
        {
            _openUrl(Url);
            return true;
        });
        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }
}
