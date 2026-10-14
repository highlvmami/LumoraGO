using LumoraGo.Core.Google;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/gmail": boşken gelen kutusundaki okunmamışlar, yazınca tüm Gmail'de arama (Gmail'in kendi arama sözdizimiyle,
/// örn. "from:ali", "has:attachment"). Enter maili tarayıcıda açar. Google bağlantısıyla çalışır, şifre gerekmez.
/// </summary>
public static class GmailPlugin
{
    public const string Keyword = "/gmail";
    private const int Shown = 20;

    public static LiveLookupPlugin Create(Gmail gmail, Action<string> open, Action? openSettings = null, Func<DateTime>? now = null) => new(
        new PluginMetadata("lumorago.gmail", "Gmail", Keyword),
        "",
        (text, host, ct) => QueryAsync(gmail, text, host, open, openSettings, (now ?? (() => DateTime.Now))(), ct),
        needsArgument: false,
        cacheFor: TimeSpan.FromMinutes(1));

    internal static async Task<IReadOnlyList<Result>> QueryAsync(
        Gmail gmail, string text, IPluginHost? host, Action<string> open, Action? openSettings, DateTime now, CancellationToken ct)
    {
        if (!gmail.IsConnected)
        {
            return [TasksPlugin.NotConnected("Gmail", openSettings)];
        }

        var search = text.Trim();
        var messages = await gmail.SearchAsync(search.Length > 0 ? search : Gmail.UnreadQuery, Shown, ct);
        var results = messages.Select((m, i) =>
        {
            var ago = m.Date is { } date ? " · " + RecentFilesPlugin.Ago(date, now) : "";
            return new Result(m.Subject, $"{(m.Unread ? "● " : "")}{m.From}{ago} · Enter ile aç", InfoPlugins.EmojiPrefix + "✉️", 1000 - i, _ =>
            {
                open(m.Url);
                return true;
            },
            [
                new ContextAction("Gönderenin adresini kopyala", _ =>
                {
                    host?.CopyToClipboard(m.FromAddress);
                    return true;
                }),
                new ContextAction($"{m.From} kişisinden gelen tüm mailler", _ =>
                {
                    host?.ChangeQuery($"{Keyword} from:{m.FromAddress}");
                    return false;
                }),
            ])
            {
                PreviewText = $"{m.Subject}\n{m.From} <{m.FromAddress}>{ago}\n\n{m.Snippet}",
                DragData = DragPayload.Url(m.Url),
            };
        }).ToList();

        if (results.Count == 0)
        {
            results.Add(new Result(search.Length > 0 ? "Eşleşen mail yok" : "Okunmamış mail yok", "Gmail'de aramak için yaz: /gmail fatura, /gmail from:ali", null, 2, _ => false));
        }

        results.Add(new Result(search.Length > 0 ? $"\"{search}\" için Gmail'de ara" : "Gmail'i aç", null, null, 1, _ =>
        {
            open(search.Length > 0 ? "https://mail.google.com/mail/u/0/#search/" + Uri.EscapeDataString(search) : "https://mail.google.com/mail/u/0/#inbox");
            return true;
        }));
        return results;
    }
}
