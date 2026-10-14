using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LumoraGo.Core.Info;

public sealed record NewsFeed(string Name, string Url)
{
    /// <summary>Ayarlardaki "Ad | adres" satırı; ad yoksa alan adı kullanılır.</summary>
    public static NewsFeed? Parse(string line)
    {
        var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
        var url = parts[^1];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var name = parts.Length > 1 && parts[0].Length > 0 ? parts[0] : uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        return new NewsFeed(name, url);
    }

    public static readonly IReadOnlyList<string> Defaults =
    [
        "BBC Türkçe | https://feeds.bbci.co.uk/turkce/rss.xml",
        "NTV | https://www.ntv.com.tr/son-dakika.rss",
        "TRT Haber | https://www.trthaber.com/sondakika.rss",
        "DW Türkçe | https://rss.dw.com/rdf/rss-tur-all",
        "Anadolu Ajansı | https://www.aa.com.tr/tr/rss/default?cat=guncel",
        "Webtekno | https://www.webtekno.com/rss.xml",
    ];
}

/// <param name="Published">Yerel saat; kaynak yazmadıysa null.</param>
public sealed record NewsItem(string Source, string Title, string Url, DateTime? Published, string Summary);

/// <summary>RSS 2.0, RSS 1.0 (RDF) ve Atom beslemelerinden son başlıkları okur. Her kaynak birkaç dakika önbellekte kalır.</summary>
public sealed partial class NewsService(HttpClient http, Func<DateTime>? utcNow = null)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<NewsItem> Items)> _cache = [];

    /// <param name="errors">Okunamayan kaynakların adı ve nedeni.</param>
    public async Task<IReadOnlyList<NewsItem>> LatestAsync(IReadOnlyList<NewsFeed> feeds, Action<string>? errors, CancellationToken ct)
    {
        var lists = await Task.WhenAll(feeds.Select(async feed =>
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(feed.Url, out var hit) && _utcNow() - hit.At < CacheFor)
                {
                    return hit.Items;
                }
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LumoraGo");
                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"sunucu {(int)response.StatusCode} döndü");
                }

                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var items = Parse(feed.Name, text);
                lock (_cache)
                {
                    _cache[feed.Url] = (_utcNow(), items);
                }

                return items;
            }
            catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                errors?.Invoke($"{feed.Name}: {(ex is TaskCanceledException ? "zaman aşımı" : ex.Message)}");
                return (IReadOnlyList<NewsItem>)[];
            }
        })).ConfigureAwait(false);

        return lists.SelectMany(l => l)
            .GroupBy(i => i.Url)
            .Select(g => g.First())
            .OrderByDescending(i => i.Published ?? DateTime.MinValue)
            .ToList();
    }

    internal static IReadOnlyList<NewsItem> Parse(string source, string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿', ' ', '\r', '\n', '\t'));
        var items = new List<NewsItem>();
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            var title = Clean(Child(e, "title"));
            var link = Child(e, "link") is { Length: > 0 } l ? l.Trim()
                : e.Elements().Where(x => x.Name.LocalName == "link")
                    .OrderByDescending(x => (string?)x.Attribute("rel") is null or "alternate")
                    .Select(x => (string?)x.Attribute("href")).FirstOrDefault(h => h is { Length: > 0 });
            link ??= Child(e, "guid") is { } guid && guid.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? guid.Trim() : null;
            if (title.Length == 0 || link is null)
            {
                continue;
            }

            var date = Date(Child(e, "pubDate") ?? Child(e, "published") ?? Child(e, "updated") ?? Child(e, "date"));
            var summary = Clean(Child(e, "description") ?? Child(e, "summary") ?? Child(e, "content") ?? "");
            items.Add(new NewsItem(source, title, link, date, summary));
        }

        return items;
    }

    private static string? Child(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;

    /// <summary>HTML etiketlerini ve kodlanmış karakterleri temizler, boşlukları teke indirir.</summary>
    internal static string Clean(string? html) =>
        html is null ? "" : Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    internal static DateTime? Date(string? text)
    {
        if (text is not { Length: > 0 })
        {
            return null;
        }

        text = text.Trim();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var iso))
        {
            return iso.LocalDateTime;
        }

        // RFC 822: "Tue, 14 Oct 2026 21:05:00 +0300" ya da "GMT"; .NET sayısal olmayan bölgeleri okuyamaz.
        var rfc = ZoneName().Replace(text, m => m.Value.Trim() switch
        {
            "GMT" or "UT" or "UTC" or "Z" => " +0000",
            "EST" => " -0500",
            "EDT" => " -0400",
            "CET" => " +0100",
            "CEST" => " +0200",
            _ => " +0000",
        });
        string[] formats = ["ddd, d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm zzz", "ddd, dd MMM yyyy HH:mm:ss zzz"];
        rfc = OffsetWithoutColon().Replace(rfc, "$1$2:$3");
        return DateTimeOffset.TryParseExact(rfc, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed.LocalDateTime
            : null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s+[A-Z]{1,4}$")]
    private static partial Regex ZoneName();

    [GeneratedRegex(@"([+-])(\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
