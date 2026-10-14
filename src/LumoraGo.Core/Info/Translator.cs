using System.Text;
using System.Text.Json;

namespace LumoraGo.Core.Info;

/// <param name="From">Algılanan kaynak dil kodu, örn. "en".</param>
public sealed record Translation(string Text, string From, string To);

/// <summary>Google Çeviri'nin anahtarsız web uç noktasıyla çeviri. Türkçe yazılırsa İngilizceye, diğer dillerden Türkçeye.</summary>
public sealed class Translator(HttpClient http)
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t";

    public static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tr"] = "Türkçe", ["en"] = "İngilizce", ["de"] = "Almanca", ["fr"] = "Fransızca", ["es"] = "İspanyolca",
        ["it"] = "İtalyanca", ["ru"] = "Rusça", ["ar"] = "Arapça", ["fa"] = "Farsça", ["ja"] = "Japonca",
        ["ko"] = "Korece", ["zh"] = "Çince", ["nl"] = "Felemenkçe", ["pt"] = "Portekizce", ["el"] = "Yunanca",
        ["az"] = "Azerice", ["uk"] = "Ukraynaca", ["pl"] = "Lehçe", ["sv"] = "İsveççe", ["ku"] = "Kürtçe",
    };

    public static string LanguageName(string code) => Languages.TryGetValue(code, out var name) ? name : code;

    /// <param name="to">null ise Türkçe metin İngilizceye, diğerleri Türkçeye çevrilir.</param>
    public async Task<Translation> TranslateAsync(string text, string? to, CancellationToken ct)
    {
        var first = await RequestAsync(text, to ?? "tr", ct).ConfigureAwait(false);
        if (to is null && first.From.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            return await RequestAsync(text, "en", ct).ConfigureAwait(false);
        }

        return first;
    }

    private async Task<Translation> RequestAsync(string text, string to, CancellationToken ct)
    {
        var url = $"{Endpoint}&sl=auto&tl={Uri.EscapeDataString(to)}&q={Uri.EscapeDataString(text)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LumoraGo");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google Çeviri {(int)response.StatusCode} döndü");
        }

        return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), to);
    }

    /// <summary>Yanıt iç içe dizi: [[["çeviri","asıl",…],…], null, "en", …].</summary>
    internal static Translation Parse(string json, string to)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var builder = new StringBuilder();
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var part in root[0].EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Array && part.GetArrayLength() > 0 && part[0].ValueKind == JsonValueKind.String)
                {
                    builder.Append(part[0].GetString());
                }
            }
        }

        var from = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String ? root[2].GetString()! : "";
        return new Translation(builder.ToString().Trim(), from, to);
    }
}
