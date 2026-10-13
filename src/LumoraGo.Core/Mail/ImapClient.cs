using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using LumoraGo.Core.Accounts;

namespace LumoraGo.Core.Mail;

/// <param name="Uid">Sunucudaki benzersiz numara (yeniler büyük).</param>
public sealed record MailHeader(long Uid, string From, string Subject, DateTimeOffset? Date);

/// <param name="Unread">Okunmamış toplam sayı.</param>
/// <param name="Latest">En yeni okunmamışlar, yeniden eskiye.</param>
public sealed record MailSummary(int Unread, IReadOnlyList<MailHeader> Latest);

public sealed class ImapException(string message) : Exception(message);

public delegate Task<Stream> ImapConnect(string host, int port, CancellationToken ct);

/// <summary>
/// Okunmamış mailleri listelemeye yetecek kadar küçük IMAP istemcisi (LOGIN, EXAMINE, SEARCH, FETCH). Gelen kutusu salt okunur
/// açılır ve gövdeler PEEK ile bile alınmaz, yani hiçbir mail "okundu" olmaz.
/// </summary>
public sealed partial class ImapClient
{
    static ImapClient()
    {
        // iso-8859-9 (Türkçe) gibi kod sayfaları varsayılan olarak yüklü değil.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<Stream> ConnectTlsAsync(string host, int port, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, ct);
            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
            return ssl;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public static async Task<MailSummary> FetchUnreadAsync(ImapConnect connect, MailAccount account, int max, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var stream = await connect(account.Host, account.Port, limit.Token);
            var session = new Session(stream, limit.Token);
            return await session.RunAsync(account, max);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ImapException("sunucu zamanında yanıt vermedi");
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.Security.Authentication.AuthenticationException)
        {
            throw new ImapException($"sunucuya bağlanılamadı ({ex.Message})");
        }
    }

    private sealed class Session(Stream stream, CancellationToken ct)
    {
        private int _tag;

        private sealed record Reply(List<(string Text, List<byte[]> Literals)> Untagged, string Status, string Text);

        public async Task<MailSummary> RunAsync(MailAccount account, int max)
        {
            var greeting = await ReadLineAsync();
            if (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase) && !greeting.StartsWith("* PREAUTH", StringComparison.OrdinalIgnoreCase))
            {
                throw new ImapException($"beklenmeyen karşılama: {greeting}");
            }

            try
            {
                var login = await CommandAsync($"LOGIN {Quote(account.Address)} {Quote(account.Password)}");
                if (login.Status != "OK")
                {
                    throw new ImapException($"giriş reddedildi ({login.Text}); Gmail için normal şifre değil, uygulama şifresi gerekir");
                }

                var examine = await CommandAsync("EXAMINE INBOX");
                if (examine.Status != "OK")
                {
                    throw new ImapException($"gelen kutusu açılamadı ({examine.Text})");
                }

                var search = await CommandAsync("UID SEARCH UNSEEN");
                var uids = search.Untagged
                    .Where(u => u.Text.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(u => u.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(2))
                    .Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)
                    .Where(n => n >= 0)
                    .Distinct()
                    .Order()
                    .ToList();
                if (uids.Count == 0)
                {
                    return new MailSummary(0, []);
                }

                var wanted = uids.TakeLast(Math.Max(1, max)).ToList();
                var fetch = await CommandAsync($"UID FETCH {string.Join(',', wanted)} (BODY.PEEK[HEADER.FIELDS (FROM SUBJECT DATE)])");
                if (fetch.Status != "OK")
                {
                    throw new ImapException($"mailler okunamadı ({fetch.Text})");
                }

                var headers = new List<MailHeader>();
                foreach (var (text, literals) in fetch.Untagged)
                {
                    if (literals.Count == 0 || UidPattern().Match(text) is not { Success: true } m)
                    {
                        continue;
                    }

                    headers.Add(ParseHeader(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Encoding.UTF8.GetString(literals[0])));
                }

                return new MailSummary(uids.Count, headers.OrderByDescending(h => h.Uid).ToList());
            }
            finally
            {
                try
                {
                    await CommandAsync("LOGOUT");
                }
                catch (Exception ex) when (ex is IOException or ImapException or OperationCanceledException or ObjectDisposedException)
                {
                    // Çıkış nezaket; bağlantı zaten kapanıyor.
                }
            }
        }

        private async Task<Reply> CommandAsync(string command)
        {
            var tag = $"A{++_tag}";
            var bytes = Encoding.UTF8.GetBytes($"{tag} {command}\r\n");
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);

            var untagged = new List<(string, List<byte[]>)>();
            while (true)
            {
                var literals = new List<byte[]>();
                var text = await ReadLineAsync();
                // "{123}" ile biten satırdan sonra 123 bayt veri gelir, ardından satır devam eder.
                while (LiteralPattern().Match(text) is { Success: true } lit)
                {
                    var data = new byte[int.Parse(lit.Groups[1].Value, CultureInfo.InvariantCulture)];
                    await stream.ReadExactlyAsync(data, ct);
                    literals.Add(data);
                    text = text[..lit.Index] + await ReadLineAsync();
                }

                if (text.StartsWith(tag + " ", StringComparison.Ordinal))
                {
                    var rest = text[(tag.Length + 1)..];
                    var space = rest.IndexOf(' ');
                    return new Reply(untagged, (space < 0 ? rest : rest[..space]).ToUpperInvariant(), space < 0 ? "" : rest[(space + 1)..].Trim());
                }

                untagged.Add((text, literals));
            }
        }

        private async Task<string> ReadLineAsync()
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(one, ct) == 0)
                {
                    throw new IOException("sunucu bağlantıyı kapattı");
                }

                if (one[0] == '\n')
                {
                    break;
                }

                bytes.Add(one[0]);
            }

            if (bytes.Count > 0 && bytes[^1] == '\r')
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            return Encoding.UTF8.GetString([.. bytes]);
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    internal static MailHeader ParseHeader(long uid, string block)
    {
        // Katlanmış başlık satırları (boşlukla başlayan devam satırları) tek satıra getirilir.
        var unfolded = Regex.Replace(block.Replace("\r\n", "\n"), @"\n[ \t]+", " ");
        string from = "", subject = "", dateText = "";
        foreach (var line in unfolded.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            switch (line[..colon].Trim().ToLowerInvariant())
            {
                case "from":
                    from = value;
                    break;
                case "subject":
                    subject = value;
                    break;
                case "date":
                    dateText = value;
                    break;
            }
        }

        var cleaned = Regex.Replace(dateText, @"\([^)]*\)", "").Trim();
        DateTimeOffset? date = DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var decodedSubject = DecodeWords(subject);
        return new MailHeader(uid, DisplayName(from), decodedSubject.Length == 0 ? "(konu yok)" : decodedSubject, date);
    }

    internal static string DisplayName(string from)
    {
        var decoded = DecodeWords(from).Trim();
        var lt = decoded.LastIndexOf('<');
        if (lt > 0)
        {
            var name = decoded[..lt].Trim().Trim('"').Trim();
            if (name.Length > 0)
            {
                return name;
            }
        }

        return decoded.Trim('<', '>', ' ');
    }

    /// <summary>RFC 2047 kodlu sözcükleri ("=?UTF-8?B?...?=", "=?iso-8859-9?Q?...?=") çözer; bitişik parçalar arasındaki boşluk atılır.</summary>
    internal static string DecodeWords(string text)
    {
        if (!text.Contains("=?", StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder();
        var last = 0;
        var previousWasEncoded = false;
        foreach (Match m in EncodedWordPattern().Matches(text))
        {
            var between = text[last..m.Index];
            if (!(previousWasEncoded && string.IsNullOrWhiteSpace(between)))
            {
                result.Append(between);
            }

            result.Append(DecodeWord(m));
            previousWasEncoded = true;
            last = m.Index + m.Length;
        }

        result.Append(text[last..]);
        return result.ToString();
    }

    private static string DecodeWord(Match m)
    {
        try
        {
            var encoding = Encoding.GetEncoding(m.Groups[1].Value, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            var payload = m.Groups[3].Value;
            byte[] bytes;
            if (string.Equals(m.Groups[2].Value, "B", StringComparison.OrdinalIgnoreCase))
            {
                bytes = Convert.FromBase64String(payload);
            }
            else
            {
                var raw = new List<byte>();
                for (var i = 0; i < payload.Length; i++)
                {
                    if (payload[i] == '_')
                    {
                        raw.Add((byte)' ');
                    }
                    else if (payload[i] == '=' && i + 2 < payload.Length && Uri.IsHexDigit(payload[i + 1]) && Uri.IsHexDigit(payload[i + 2]))
                    {
                        raw.Add(Convert.ToByte(payload.Substring(i + 1, 2), 16));
                        i += 2;
                    }
                    else
                    {
                        raw.Add((byte)payload[i]);
                    }
                }

                bytes = [.. raw];
            }

            return encoding.GetString(bytes);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return m.Value;
        }
    }

    [GeneratedRegex(@"\{(\d+)\}$")]
    private static partial Regex LiteralPattern();

    [GeneratedRegex(@"\bUID (\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UidPattern();

    [GeneratedRegex(@"=\?([\w\-]+)\?([BbQq])\?([^?]*)\?=")]
    private static partial Regex EncodedWordPattern();
}
