using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/yt kedi" gibi bir tetik kelimesiyle varsayılan tarayıcıda arama yapar.</summary>
public sealed class WebSearchPlugin : IPlugin
{
    private readonly string _siteName;
    private readonly string _searchUrl;
    private readonly string _homeUrl;
    private readonly Action<string> _openUrl;

    /// <param name="searchUrl">Aranan metnin yerine "{0}" yazılan adres, örn. "https://www.youtube.com/results?search_query={0}".</param>
    /// <param name="homeUrl">Arama metni boşken açılacak adres.</param>
    /// <param name="openUrl">Adresi varsayılan tarayıcıda açar.</param>
    public WebSearchPlugin(PluginMetadata metadata, string siteName, string searchUrl, string homeUrl, Action<string> openUrl)
    {
        Metadata = metadata;
        _siteName = siteName;
        _searchUrl = searchUrl;
        _homeUrl = homeUrl;
        _openUrl = openUrl;
    }

    public PluginMetadata Metadata { get; }

    public static WebSearchPlugin YouTube(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.youtube", "YouTube'da ara", "/yt"),
        "YouTube",
        "https://www.youtube.com/results?search_query={0}",
        "https://www.youtube.com",
        openUrl);

    public static WebSearchPlugin Google(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.google", "Google'da ara", "/gg"),
        "Google",
        "https://www.google.com/search?q={0}",
        "https://www.google.com",
        openUrl);

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var url = BuildUrl(query.Search);
        var result = query.IsEmpty
            ? new Result($"{_siteName}'u aç", $"Aramak için \"{Metadata.ActionKeyword} \" sonrasına bir şey yaz", null, 100, Open(url))
            : new Result($"{_siteName}'da ara: {query.Search}", url, null, 100, Open(url));

        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }

    public string BuildUrl(string search) =>
        search.Length == 0 ? _homeUrl : string.Format(_searchUrl, Uri.EscapeDataString(search));

    private Func<ActionContext, bool> Open(string url) => _ =>
    {
        _openUrl(url);
        return true;
    };
}
