using System.Globalization;
using System.Text.Json;

namespace LumoraGo.Core.Info;

/// <param name="Kind">"movie" ya da "tv".</param>
/// <param name="Rating">TMDB kullanıcı puanı (10 üzerinden); oy yoksa null.</param>
/// <param name="Streaming">Türkiye'de abonelikle izlenebildiği platformlar.</param>
public sealed record MovieInfo(
    int Id,
    string Kind,
    string Title,
    string? OriginalTitle,
    int? Year,
    double? Rating,
    int Votes,
    string Overview,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Streaming,
    IReadOnlyList<string> RentOrBuy)
{
    public string Url => $"https://www.themoviedb.org/{Kind}/{Id}?language=tr-TR";

    public string KindName => Kind == "tv" ? "Dizi" : "Film";
}

/// <summary>Film ve dizi bilgisi: The Movie Database (TMDB). Kullanıcının ücretsiz API anahtarı gerekir.</summary>
public sealed class MovieService(HttpClient http, Func<string?> apiKey)
{
    private const string Base = "https://api.themoviedb.org/3/";

    public bool IsConfigured => apiKey() is { Length: > 0 };

    /// <summary>İlk birkaç sonucun türleri ve Türkiye'deki platformları da alınır.</summary>
    public async Task<IReadOnlyList<MovieInfo>> SearchAsync(string query, int detailed, CancellationToken ct)
    {
        using var search = await GetAsync($"search/multi?query={Uri.EscapeDataString(query)}&language=tr-TR&include_adult=false", ct).ConfigureAwait(false);
        var found = ParseSearch(search.RootElement);
        var details = await Task.WhenAll(found.Take(detailed).Select(async m =>
        {
            try
            {
                using var doc = await GetAsync($"{m.Kind}/{m.Id}?language=tr-TR&append_to_response=watch/providers", ct).ConfigureAwait(false);
                return WithDetails(m, doc.RootElement);
            }
            catch (HttpRequestException)
            {
                return m;
            }
        })).ConfigureAwait(false);
        return [.. details, .. found.Skip(detailed)];
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        var key = apiKey()?.Trim() ?? "";
        using var request = new HttpRequestMessage(HttpMethod.Get, Base + path);
        // Uzun "okuma erişim anahtarı" (JWT) başlıkta, kısa v3 anahtarı adreste gider.
        if (key.StartsWith("eyJ", StringComparison.Ordinal))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }
        else
        {
            request.RequestUri = new Uri(Base + path + (path.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(key));
        }

        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new HttpRequestException("TMDB anahtarı geçersiz; Ayarlar → İnternet'ten kontrol et");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"TMDB {(int)response.StatusCode} döndü");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<MovieInfo> ParseSearch(JsonElement root) =>
        root.Arr("results")
            .Where(r => r.Str("media_type") is "movie" or "tv")
            .Select(r =>
            {
                var kind = r.Str("media_type")!;
                var title = (kind == "tv" ? r.Str("name") : r.Str("title")) ?? "";
                var original = kind == "tv" ? r.Str("original_name") : r.Str("original_title");
                var date = kind == "tv" ? r.Str("first_air_date") : r.Str("release_date");
                var votes = (int)(r.Num("vote_count") ?? 0);
                return new MovieInfo(
                    (int)(r.Num("id") ?? 0), kind, title, original == title ? null : original,
                    date is { Length: >= 4 } && int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null,
                    votes > 0 ? r.Num("vote_average") : null, votes, r.Str("overview") ?? "", [], [], []);
            })
            .Where(m => m.Id > 0 && m.Title.Length > 0)
            .ToList();

    internal static MovieInfo WithDetails(MovieInfo movie, JsonElement root)
    {
        var tr = root.Obj("watch/providers").Obj("results").Obj("TR");
        static IReadOnlyList<string> Names(JsonElement e, params string[] keys) =>
            keys.SelectMany(k => e.Arr(k)).Select(p => p.Str("provider_name")).OfType<string>().Distinct().ToList();
        return movie with
        {
            Genres = root.Arr("genres").Select(g => g.Str("name")).OfType<string>().ToList(),
            Streaming = Names(tr, "flatrate", "free", "ads"),
            RentOrBuy = Names(tr, "rent", "buy"),
            Overview = movie.Overview.Length > 0 ? movie.Overview : root.Str("overview") ?? "",
        };
    }
}
