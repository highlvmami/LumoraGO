using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumoraGo.Core.Network;

public sealed record PingReplyInfo(bool Success, long RoundTripMs, string Status, string? Address);

public delegate Task<PingReplyInfo> PingFunc(string host, int timeoutMs, CancellationToken ct);

/// <summary>Klasik WHOIS (TCP 43): sunucuya sorguyu gönderir, tüm yanıtı metin olarak döndürür.</summary>
public delegate Task<string> WhoisQuery(string server, string query, CancellationToken ct);

/// <summary>
/// /ping, /dns, /whois ve /disip işleri. DNS için DNS-over-HTTPS (Cloudflare), whois için RDAP (rdap.org)
/// kullanılır; RDAP'ın bulunmadığı uzantılarda (örn. .tr) klasik WHOIS'e düşülür. Hiçbiri anahtar gerektirmez.
/// </summary>
public sealed partial class NetworkTools(
    HttpClient http,
    PingFunc ping,
    WhoisQuery whois,
    TimeSpan? pingGap = null,
    Func<DateTime>? utcNow = null)
{
    public const string DohUrl = "https://cloudflare-dns.com/dns-query";
    public const string RdapUrl = "https://rdap.org";
    public const string IpInfoUrl = "https://ipwho.is/";
    public const string IpInfoFallbackUrl = "https://ipapi.co/json/";

    private const int PingCount = 4;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly string[] DefaultDnsTypes = ["A", "AAAA", "MX", "NS", "TXT"];

    private static readonly Dictionary<string, int> DnsTypeNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 1, ["NS"] = 2, ["CNAME"] = 5, ["SOA"] = 6, ["PTR"] = 12, ["MX"] = 15, ["TXT"] = 16,
        ["AAAA"] = 28, ["SRV"] = 33, ["CAA"] = 257,
    };

    private readonly TimeSpan _pingGap = pingGap ?? TimeSpan.FromMilliseconds(500);
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostPattern();

    /// <summary>"https://www.Örnek.com:8080/yol" → "www.xn--rnek-...com"; IP'ler olduğu gibi kalır.</summary>
    public static bool TryNormalizeHost(string input, out string host)
    {
        host = "";
        var text = input.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            text = text[(scheme + 3)..];
        }

        var end = text.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            text = text[..end];
        }

        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            text = text[(at + 1)..];
        }

        if (text.StartsWith('[') && text.IndexOf(']') is var close and > 0)
        {
            text = text[1..close];
        }
        else if (text.Count(c => c == ':') == 1)
        {
            text = text[..text.IndexOf(':')];
        }

        text = text.Trim().TrimEnd('.');
        if (text.Length == 0 || text.Contains(' '))
        {
            return false;
        }

        if (IPAddress.TryParse(text, out var address))
        {
            host = address.ToString();
            return true;
        }

        try
        {
            text = new IdnMapping().GetAscii(text).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (text.Length > 253 || !HostPattern().IsMatch(text))
        {
            return false;
        }

        host = text;
        return true;
    }

    public async Task PingAsync(string argument, Action<IReadOnlyList<NetLine>> report, CancellationToken ct)
    {
        var host = RequireHost(argument, "/ping google.com");
        var replies = new List<NetLine>();
        var times = new List<long>();
        for (var i = 0; i < PingCount; i++)
        {
            var reply = await ping(host, 2000, ct);
            if (reply.Success)
            {
                times.Add(reply.RoundTripMs);
                replies.Add(new NetLine($"{reply.Address ?? host}: {reply.RoundTripMs} ms", $"{i + 1}. yanıt", $"{reply.RoundTripMs} ms"));
            }
            else
            {
                replies.Add(new NetLine($"{i + 1}. yanıt yok", reply.Status));
            }

            report([PingSummary(i + 1, times), .. replies]);
            if (i < PingCount - 1 && _pingGap > TimeSpan.Zero)
            {
                await Task.Delay(_pingGap, ct);
            }
        }
    }

    internal static NetLine PingSummary(int sent, IReadOnlyList<long> times)
    {
        if (times.Count == 0)
        {
            return new NetLine($"Yanıt gelmedi ({sent} denemede)", "Adres kapalı ya da ping'e kapalı olabilir");
        }

        var loss = 100 * (sent - times.Count) / sent;
        var text = $"Ortalama {times.Average():0} ms · en az {times.Min()} · en çok {times.Max()} · kayıp %{loss}";
        return new NetLine(text, $"{sent} gönderildi", text);
    }

    public async Task DnsAsync(string argument, Action<IReadOnlyList<NetLine>> report, CancellationToken ct)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var host = RequireHost(parts.FirstOrDefault() ?? "", "/dns google.com mx");
        var types = DefaultDnsTypes;
        if (parts.Length > 1)
        {
            var requested = parts[1].ToUpperInvariant();
            if (!DnsTypeNumbers.ContainsKey(requested))
            {
                throw new InvalidOperationException($"Bilinmeyen kayıt türü: {parts[1]}. Şunlardan biri: {string.Join(", ", DnsTypeNumbers.Keys)}");
            }

            types = [requested];
        }
        else if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // IP verilince ters sorgu: 8.8.8.8 → 8.8.8.8.in-addr.arpa PTR
            host = string.Join('.', host.Split('.').Reverse()) + ".in-addr.arpa";
            types = ["PTR"];
        }

        var answers = await Task.WhenAll(types.Select(t => DohAsync(host, t, ct)));
        var lines = new List<NetLine>();
        var nxdomain = answers.All(a => a.Status == 3);
        foreach (var (_, records) in answers.Select(a => (a.Status, a.Records)))
        {
            lines.AddRange(records);
        }

        if (nxdomain)
        {
            throw new InvalidOperationException("Alan adı bulunamadı.");
        }

        if (lines.Count == 0)
        {
            lines.Add(new NetLine("Kayıt yok", types.Length == 1 ? $"{types[0]} kaydı bulunamadı" : null));
        }

        report(lines);
    }

    private async Task<(int Status, List<NetLine> Records)> DohAsync(string name, string type, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{DohUrl}?name={Uri.EscapeDataString(name)}&type={type}");
        request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return ParseDoh(await response.Content.ReadAsStringAsync(ct));
    }

    internal static (int Status, List<NetLine> Records) ParseDoh(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = root.TryGetProperty("Status", out var s) ? s.GetInt32() : 0;
        var records = new List<NetLine>();
        if (root.TryGetProperty("Answer", out var answer) && answer.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in answer.EnumerateArray())
            {
                var typeNumber = item.TryGetProperty("type", out var t) ? t.GetInt32() : 0;
                var typeName = DnsTypeNumbers.FirstOrDefault(kv => kv.Value == typeNumber).Key ?? $"TYPE{typeNumber}";
                var data = item.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
                data = CleanDnsData(typeName, data);
                var ttl = item.TryGetProperty("TTL", out var l) ? l.GetInt32() : 0;
                records.Add(new NetLine($"{typeName}  {data}", $"TTL {ttl} sn", data));
            }
        }

        return (status, records);
    }

    private static string CleanDnsData(string type, string data)
    {
        if (type == "TXT")
        {
            return data.Length >= 2 && data[0] == '"' && data[^1] == '"' ? data[1..^1] : data;
        }

        return type is "NS" or "CNAME" or "PTR" or "MX" or "SRV" ? data.TrimEnd('.') : data;
    }

    public async Task WhoisAsync(string argument, Action<IReadOnlyList<NetLine>> report, CancellationToken ct)
    {
        var host = RequireHost(argument, "/whois google.com");
        var isIp = IPAddress.TryParse(host, out _);

        // Önce RDAP: "www.ornek.com" 404 verirse "ornek.com"a düş.
        foreach (var candidate in isIp ? [host] : Candidates(host))
        {
            using var response = await http.GetAsync($"{RdapUrl}/{(isIp ? "ip" : "domain")}/{Uri.EscapeDataString(candidate)}", ct);
            if (response.IsSuccessStatusCode)
            {
                report(ParseRdap(await response.Content.ReadAsStringAsync(ct), _utcNow()));
                return;
            }
        }

        if (isIp)
        {
            throw new InvalidOperationException("Bu IP için kayıt bulunamadı.");
        }

        report(await ClassicWhoisAsync(host, ct));
    }

    private static IEnumerable<string> Candidates(string host)
    {
        var labels = host.Split('.');
        for (var skip = 0; labels.Length - skip >= 2 && skip < 4; skip++)
        {
            yield return string.Join('.', labels.Skip(skip));
        }
    }

    /// <summary>RDAP'ın olmadığı uzantılar için: IANA'dan uzantının WHOIS sunucusunu bul, oradan sorgula.</summary>
    private async Task<List<NetLine>> ClassicWhoisAsync(string host, CancellationToken ct)
    {
        var tld = host[(host.LastIndexOf('.') + 1)..];
        var referral = await whois("whois.iana.org", tld, ct);
        var server = referral.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("whois:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("refer:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .FirstOrDefault(s => s.Length > 0);
        if (server is null)
        {
            throw new InvalidOperationException($".{tld} için WHOIS sunucusu bulunamadı.");
        }

        foreach (var candidate in Candidates(host))
        {
            var text = await whois(server, candidate, ct);
            var lines = ParseWhoisText(text);
            if (lines.Count > 0 && !NotFoundPattern().IsMatch(text))
            {
                return lines;
            }
        }

        throw new InvalidOperationException("Alan adı kaydı bulunamadı (kayıtsız olabilir).");
    }

    [GeneratedRegex(@"no match|not found|no entries found|no data found|NOT FOUND", RegexOptions.IgnoreCase)]
    private static partial Regex NotFoundPattern();

    internal static List<NetLine> ParseWhoisText(string text)
    {
        var lines = new List<NetLine>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim().TrimStart('*', ' ');
            if (line.Length == 0 || line[0] is '%' or '#' or '>' or '-' or '=')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon is > 0 and <= 40 && colon < line.Length - 1)
            {
                var value = line[(colon + 1)..].Trim();
                if (value.Length > 0)
                {
                    lines.Add(new NetLine(value, line[..colon].Trim(), value));
                    continue;
                }
            }

            lines.Add(new NetLine(line));
            if (lines.Count >= 30)
            {
                break;
            }
        }

        return lines;
    }

    internal static List<NetLine> ParseRdap(string json, DateTime utcNow)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var lines = new List<NetLine>();

        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var name = Str(root, "ldhName") ?? Str(root, "name") ?? Str(root, "handle");
        if (name is not null)
        {
            lines.Add(new NetLine(name.ToLowerInvariant(), root.TryGetProperty("ldhName", out _) ? "Alan adı" : "Ağ adı", name));
        }

        if (Str(root, "startAddress") is { } start && Str(root, "endAddress") is { } end)
        {
            lines.Add(new NetLine($"{start} – {end}", "Adres aralığı", $"{start}-{end}"));
        }

        if (Str(root, "country") is { } country)
        {
            lines.Add(new NetLine(country, "Ülke"));
        }

        if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in entities.EnumerateArray())
            {
                var roles = entity.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
                    ? r.EnumerateArray().Select(x => x.GetString()).ToList()
                    : [];
                var fn = VcardName(entity);
                if (fn is not null && roles.Contains("registrar"))
                {
                    lines.Add(new NetLine(fn, "Kayıt kuruluşu (registrar)", fn));
                }
            }
        }

        if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var ev in events.EnumerateArray())
            {
                if (!DateTime.TryParse(Str(ev, "eventDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                {
                    continue;
                }

                var date = when.ToString("dd.MM.yyyy", Turkish);
                switch (Str(ev, "eventAction"))
                {
                    case "registration":
                        lines.Add(new NetLine(date, "Kayıt tarihi", date));
                        break;
                    case "expiration":
                        var days = (int)Math.Floor((when - utcNow).TotalDays);
                        var left = days >= 0 ? $"{days} gün kaldı" : $"{-days} gün önce doldu";
                        lines.Add(new NetLine($"{date} ({left})", "Bitiş tarihi", date));
                        break;
                    case "last changed":
                        lines.Add(new NetLine(date, "Son değişiklik", date));
                        break;
                }
            }
        }

        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Array)
        {
            var values = status.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToList();
            if (values.Count > 0)
            {
                lines.Add(new NetLine(string.Join(", ", values), "Durum"));
            }
        }

        if (root.TryGetProperty("nameservers", out var servers) && servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in servers.EnumerateArray())
            {
                if (Str(server, "ldhName") is { } ns)
                {
                    lines.Add(new NetLine(ns.ToLowerInvariant(), "Name server", ns.ToLowerInvariant()));
                }
            }
        }

        return lines;
    }

    private static string? VcardName(JsonElement entity)
    {
        if (!entity.TryGetProperty("vcardArray", out var vcard) || vcard.ValueKind != JsonValueKind.Array || vcard.GetArrayLength() < 2)
        {
            return null;
        }

        foreach (var field in vcard[1].EnumerateArray())
        {
            if (field.ValueKind == JsonValueKind.Array && field.GetArrayLength() >= 4 && field[0].GetString() == "fn")
            {
                return field[3].GetString();
            }
        }

        return null;
    }

    public async Task MyIpAsync(Action<IReadOnlyList<NetLine>> report, CancellationToken ct)
    {
        Exception? first = null;
        foreach (var url in new[] { IpInfoUrl, IpInfoFallbackUrl })
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                var lines = ParseIpInfo(await response.Content.ReadAsStringAsync(ct));
                if (lines.Count > 0)
                {
                    report(lines);
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                first ??= ex;
            }
        }

        throw new InvalidOperationException($"Dış IP alınamadı, internet bağlantını kontrol et. ({first?.Message})");
    }

    internal static List<NetLine> ParseIpInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            return [];
        }

        var ip = Str(root, "ip");
        if (ip is null)
        {
            return [];
        }

        var lines = new List<NetLine> { new(ip, "Dış IP adresi", ip) };
        var place = string.Join(", ", new[] { Str(root, "city"), Str(root, "region"), Str(root, "country") ?? Str(root, "country_name") }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (place.Length > 0)
        {
            lines.Add(new NetLine(place, "Konum (yaklaşık)", place));
        }

        string? isp = null;
        if (root.TryGetProperty("connection", out var connection) && connection.ValueKind == JsonValueKind.Object)
        {
            isp = Str(connection, "isp") ?? Str(connection, "org");
        }

        isp ??= Str(root, "org");
        if (isp is not null)
        {
            lines.Add(new NetLine(isp, "Servis sağlayıcı", isp));
        }

        var zone = root.TryGetProperty("timezone", out var tz)
            ? tz.ValueKind == JsonValueKind.Object ? Str(tz, "id") : tz.GetString()
            : null;
        if (zone is not null)
        {
            lines.Add(new NetLine(zone, "Saat dilimi"));
        }

        return lines;
    }

    private static string RequireHost(string argument, string example) =>
        TryNormalizeHost(argument, out var host)
            ? host
            : throw new InvalidOperationException($"Geçerli bir alan adı ya da IP yaz. Örnek: {example}");
}

/// <summary>Gerçek ping ve WHOIS (TCP 43) uygulamaları.</summary>
public static class SystemNetwork
{
    public static async Task<PingReplyInfo> PingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: ct);
        return reply.Status == IPStatus.Success
            ? new PingReplyInfo(true, reply.RoundtripTime, "Yanıt geldi", reply.Address?.ToString())
            : new PingReplyInfo(false, 0, StatusText(reply.Status), null);
    }

    public static async Task<string> WhoisAsync(string server, string query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync(server, 43, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(query + "\r\n"), timeout.Token);

        var buffer = new byte[8192];
        using var result = new MemoryStream();
        int read;
        while (result.Length < 64 * 1024 && (read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            result.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(result.ToArray());
    }

    private static string StatusText(IPStatus status) => status switch
    {
        IPStatus.TimedOut => "Zaman aşımı",
        IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable => "Ulaşılamıyor",
        IPStatus.TtlExpired => "TTL doldu",
        _ => status.ToString(),
    };
}
