using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumoraGo.Core.Google;

/// <param name="From">Gönderenin adı (yoksa adresi).</param>
public sealed record GmailMessage(string Id, string ThreadId, string From, string FromAddress, string Subject, string Snippet, DateTime? Date, bool Unread)
{
    public string Url => $"https://mail.google.com/mail/u/0/#all/{ThreadId}";
}

/// <summary>Gmail'i yalnızca okur: okunmamışlar, arama ve yeni mail bildirimi için. Hiçbir mail değiştirilmez.</summary>
public sealed partial class Gmail(GoogleApi api)
{
    public const string Scope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string UnreadQuery = "is:unread in:inbox";
    private const string Base = "https://gmail.googleapis.com/gmail/v1/users/me/";

    public bool IsConnected => api.IsConnected;

    /// <param name="query">Gmail arama sözdizimi, örn. "from:ali fatura".</param>
    public async Task<IReadOnlyList<GmailMessage>> SearchAsync(string query, int max, CancellationToken ct)
    {
        using var list = await api.GetAsync(GoogleService.Gmail, $"{Base}messages?maxResults={max}&q={Uri.EscapeDataString(query)}", ct);
        var ids = list.RootElement.Items("messages").Select(m => m.Str("id")).OfType<string>().ToList();
        var messages = await Task.WhenAll(ids.Select(async id =>
        {
            using var doc = await api.GetAsync(GoogleService.Gmail,
                $"{Base}messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject", ct);
            return ParseMessage(doc.RootElement);
        }));
        return messages.OfType<GmailMessage>().ToList();
    }

    /// <summary>Gelen kutusundaki okunmamış konuşma sayısı.</summary>
    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        using var doc = await api.GetAsync(GoogleService.Gmail, Base + "labels/INBOX", ct);
        return doc.RootElement.TryGetProperty("threadsUnread", out var n) && n.TryGetInt32(out var count) ? count : 0;
    }

    internal static GmailMessage? ParseMessage(JsonElement root)
    {
        if (root.Str("id") is not { } id)
        {
            return null;
        }

        var headers = root.Obj("payload").Items("headers").ToList();
        string? Header(string name) => headers.FirstOrDefault(h => string.Equals(h.Str("name"), name, StringComparison.OrdinalIgnoreCase)).Str("value");
        var (fromName, fromAddress) = SplitFrom(Header("From") ?? "");
        DateTime? date = root.Str("internalDate") is { } ms && long.TryParse(ms, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime
            : null;
        var unread = root.Items("labelIds").Any(l => l.GetString() == "UNREAD");
        var subject = Header("Subject") is { Length: > 0 } s ? s.Trim() : "(konusuz)";
        return new GmailMessage(id, root.Str("threadId") ?? id, fromName, fromAddress, subject, WebUtility.HtmlDecode(root.Str("snippet") ?? ""), date, unread);
    }

    /// <summary>"Ali Veli &lt;ali@ornek.com&gt;" → ("Ali Veli", "ali@ornek.com").</summary>
    internal static (string Name, string Address) SplitFrom(string from)
    {
        var match = AngleAddress().Match(from);
        if (!match.Success)
        {
            return (from.Trim(), from.Trim());
        }

        var address = match.Groups[2].Value.Trim();
        var name = match.Groups[1].Value.Trim().Trim('"').Trim();
        return (name.Length > 0 ? name : address, address);
    }

    [GeneratedRegex("^(.*)<([^>]+)>\\s*$")]
    private static partial Regex AngleAddress();
}

/// <summary>Yeni gelen okunmamış mailleri bulur; ilk kontrolde mevcutları "görülmüş" sayar ki açılışta bildirim yağmasın.</summary>
public sealed class GmailWatcher(Func<CancellationToken, Task<IReadOnlyList<GmailMessage>>> fetchUnread)
{
    private readonly HashSet<string> _seen = [];
    private bool _seeded;

    public async Task<IReadOnlyList<GmailMessage>> CheckAsync(CancellationToken ct)
    {
        var unread = await fetchUnread(ct);
        var fresh = _seeded ? unread.Where(m => !_seen.Contains(m.Id)).ToList() : [];
        foreach (var message in unread)
        {
            _seen.Add(message.Id);
        }

        _seeded = true;
        return fresh;
    }
}
