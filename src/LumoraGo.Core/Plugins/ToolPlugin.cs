using System.Security.Cryptography;
using System.Text;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Bir araç satırı: ne olduğu ve kopyalanacak değer.</summary>
public sealed record ToolOutput(string Value, string Label);

/// <summary>
/// "/uuid", "/sifre 20", "/b64 metin" gibi küçük geliştirici araçları. Her satır Enter ile panoya kopyalanır.
/// </summary>
public sealed class ToolPlugin(PluginMetadata metadata, string hint, Func<string, IReadOnlyList<ToolOutput>> run) : IPlugin
{
    private IPluginHost? _host;

    public PluginMetadata Metadata { get; } = metadata;

    public static IReadOnlyList<ToolPlugin> All() =>
    [
        new(new PluginMetadata("lumorago.tool.uuid", "UUID üret", "/uuid"), "",
            _ => [new(Guid.NewGuid().ToString(), "UUID"), new(Guid.NewGuid().ToString("N"), "Tiresiz UUID")]),
        new(new PluginMetadata("lumorago.tool.password", "Güçlü şifre üret", "/sifre"), "Uzunluk yazılabilir: /sifre 24",
            text => Passwords(text)),
        new(new PluginMetadata("lumorago.tool.base64", "Base64 çevir", "/b64"), "Örnek: /b64 merhaba",
            text => Base64(text)),
        new(new PluginMetadata("lumorago.tool.url", "URL kodla/çöz", "/url"), "Örnek: /url şeker & çay",
            text => UrlCoding(text)),
        new(new PluginMetadata("lumorago.tool.hash", "Özet (hash) hesapla", "/hash"), "Örnek: /hash merhaba",
            text => Hashes(text)),
        new(new PluginMetadata("lumorago.tool.count", "Karakter ve kelime say", "/say"), "Örnek: /say bir metin",
            text => Counts(text)),
    ];

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var outputs = run(query.Search);
        if (outputs.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([new Result(Metadata.Name, hint, null, 1, _ => false)]);
        }

        var results = outputs
            .Select((o, i) => new Result(o.Value, $"{o.Label} · Enter ile kopyala", null, outputs.Count - i, _ =>
            {
                _host?.CopyToClipboard(o.Value);
                return true;
            }))
            .ToList();
        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    internal static IReadOnlyList<ToolOutput> Passwords(string text)
    {
        var length = int.TryParse(text.Trim(), out var n) ? Math.Clamp(n, 6, 128) : 20;
        const string letters = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*-_=+?";
        return
        [
            new(Generate(length, letters + digits + symbols), $"{length} karakter, harf, rakam ve sembol"),
            new(Generate(length, letters + digits), $"{length} karakter, sadece harf ve rakam"),
            new(Generate(Math.Min(length, 12), digits), "PIN / sadece rakam"),
        ];
    }

    internal static IReadOnlyList<ToolOutput> Base64(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var outputs = new List<ToolOutput> { new(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), "Base64'e çevrildi") };
        if (TryDecodeBase64(text, out var decoded))
        {
            outputs.Insert(0, new(decoded, "Base64'ten çözüldü"));
        }

        return outputs;
    }

    internal static IReadOnlyList<ToolOutput> UrlCoding(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var outputs = new List<ToolOutput> { new(Uri.EscapeDataString(text), "URL kodlandı") };
        var decoded = Uri.UnescapeDataString(text.Replace('+', ' '));
        if (decoded != text)
        {
            outputs.Insert(0, new(decoded, "URL kodu çözüldü"));
        }

        return outputs;
    }

    internal static IReadOnlyList<ToolOutput> Hashes(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        return
        [
            new(Convert.ToHexStringLower(SHA256.HashData(bytes)), "SHA-256"),
            new(Convert.ToHexStringLower(SHA1.HashData(bytes)), "SHA-1"),
            new(Convert.ToHexStringLower(MD5.HashData(bytes)), "MD5"),
        ];
    }

    internal static IReadOnlyList<ToolOutput> Counts(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return
        [
            new(text.Length.ToString(), "karakter"),
            new(text.Count(c => !char.IsWhiteSpace(c)).ToString(), "boşluksuz karakter"),
            new(words.ToString(), "kelime"),
        ];
    }

    private static bool TryDecodeBase64(string text, out string decoded)
    {
        decoded = "";
        var trimmed = text.Trim();
        if (trimmed.Length < 4 || trimmed.Length % 4 != 0)
        {
            return false;
        }

        var buffer = new byte[trimmed.Length];
        if (!Convert.TryFromBase64String(trimmed, buffer, out var written))
        {
            return false;
        }

        try
        {
            decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, written);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        // Çözülen metin okunabilir değilse (ikili veri) gösterme.
        return decoded.Length > 0 && decoded.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t');
    }

    private static string Generate(int length, string alphabet) =>
        string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
}
