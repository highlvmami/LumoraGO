using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/yt kedi" gibi bir tetik kelimesiyle varsayılan tarayıcıda arama yapar.</summary>
public sealed class WebSearchPlugin : IPlugin
{
    private readonly string _searchUrl;
    private readonly string _homeUrl;
    private readonly Action<string> _openUrl;
    private readonly string _homeTitle;
    private readonly string _searchTitle;

    /// <param name="searchUrl">Aranan metnin yerine "{0}" yazılan adres, örn. "https://www.youtube.com/results?search_query={0}".</param>
    /// <param name="homeUrl">Arama metni boşken açılacak adres.</param>
    /// <param name="openUrl">Adresi varsayılan tarayıcıda açar.</param>
    /// <param name="homeTitle">Boş aramada satır başlığı; Türkçe ek siteye göre değiştiği için ayrı verilebilir.</param>
    /// <param name="searchTitle">Arama satırının başı, örn. "Vikipedi'de ara".</param>
    public WebSearchPlugin(
        PluginMetadata metadata,
        string siteName,
        string searchUrl,
        string homeUrl,
        Action<string> openUrl,
        string? homeTitle = null,
        string? searchTitle = null)
    {
        _homeTitle = homeTitle ?? $"{siteName}'u aç";
        _searchTitle = searchTitle ?? $"{siteName}'da ara";
        Metadata = metadata;
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

    public static WebSearchPlugin Translate(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.translate", "Google Çeviri", "/tr"),
        "Google Çeviri",
        "https://translate.google.com/?sl=auto&tl=tr&text={0}&op=translate",
        "https://translate.google.com/?sl=auto&tl=tr",
        openUrl,
        "Google Çeviri'yi aç",
        "Türkçeye çevir");

    public static WebSearchPlugin Wikipedia(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.wikipedia", "Vikipedi'de ara", "/wiki"),
        "Vikipedi",
        "https://tr.wikipedia.org/w/index.php?search={0}",
        "https://tr.wikipedia.org",
        openUrl,
        "Vikipedi'yi aç",
        "Vikipedi'de ara");

    public static WebSearchPlugin Maps(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.maps", "Haritalarda ara", "/harita"),
        "Google Haritalar",
        "https://www.google.com/maps/search/{0}",
        "https://www.google.com/maps",
        openUrl,
        "Google Haritalar'ı aç",
        "Haritalarda ara");

    public static WebSearchPlugin GitHubSearch(Action<string> openUrl) => new(
        new PluginMetadata("lumorago.githubsearch", "GitHub'da ara", "/gh"),
        "GitHub",
        "https://github.com/search?q={0}",
        "https://github.com",
        openUrl,
        "GitHub'ı aç",
        "GitHub'da ara");

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var url = BuildUrl(query.Search);
        var result = query.IsEmpty
            ? new Result(_homeTitle, $"Aramak için \"{Metadata.ActionKeyword} \" sonrasına bir şey yaz", null, 100, Open(url))
            : new Result($"{_searchTitle}: {query.Search}", url, null, 100, Open(url));

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
