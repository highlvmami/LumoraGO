using LumoraGo.Core.Accounts;

namespace LumoraGo.Core.Mail;

/// <param name="WebmailUrl">Enter'a basınca açılacak gelen kutusu adresi.</param>
public sealed record MailProvider(string Host, int Port, string WebmailUrl);

/// <summary>Adresin alan adından IMAP sunucusunu tahmin eder, böylece kullanıcı yalnızca adres ve şifre girer.</summary>
public static class MailProviders
{
    private static readonly Dictionary<string, MailProvider> ByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail.com"] = new("imap.gmail.com", 993, "https://mail.google.com/mail/u/0/#inbox"),
        ["googlemail.com"] = new("imap.gmail.com", 993, "https://mail.google.com/mail/u/0/#inbox"),
        ["yahoo.com"] = new("imap.mail.yahoo.com", 993, "https://mail.yahoo.com/"),
        ["yahoo.com.tr"] = new("imap.mail.yahoo.com", 993, "https://mail.yahoo.com/"),
        ["ymail.com"] = new("imap.mail.yahoo.com", 993, "https://mail.yahoo.com/"),
        ["yandex.com"] = new("imap.yandex.com", 993, "https://mail.yandex.com/"),
        ["yandex.com.tr"] = new("imap.yandex.com", 993, "https://mail.yandex.com.tr/"),
        ["yandex.ru"] = new("imap.yandex.com", 993, "https://mail.yandex.ru/"),
        ["icloud.com"] = new("imap.mail.me.com", 993, "https://www.icloud.com/mail"),
        ["me.com"] = new("imap.mail.me.com", 993, "https://www.icloud.com/mail"),
        ["mac.com"] = new("imap.mail.me.com", 993, "https://www.icloud.com/mail"),
    };

    private static readonly string[] MicrosoftDomains =
        ["outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.com.tr", "hotmail.com.tr", "live.com.tr"];

    /// <summary>Bilinen sağlayıcı yoksa null; o zaman sunucuyu kullanıcı yazmalıdır.</summary>
    public static MailProvider? Find(string address) => ByDomain.GetValueOrDefault(Domain(address) ?? "");

    /// <summary>
    /// Microsoft kişisel hesapları (Outlook/Hotmail) ve Microsoft 365 kutuları düz IMAP şifresiyle girişe kapalı;
    /// yalnızca uygulama kaydı isteyen OAuth çalışıyor. Bu yüzden desteklenmiyor.
    /// </summary>
    public static bool IsMicrosoft(string address) =>
        Domain(address) is { } d && MicrosoftDomains.Contains(d, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gelen kutusunu tarayıcıda açmak için adres; bilinmiyorsa null.</summary>
    public static string? WebmailFor(MailAccount account) =>
        Find(account.Address)?.WebmailUrl
        ?? ByDomain.Values.FirstOrDefault(p => string.Equals(p.Host, account.Host, StringComparison.OrdinalIgnoreCase))?.WebmailUrl;

    private static string? Domain(string address)
    {
        var at = address.LastIndexOf('@');
        return at > 0 && at < address.Length - 1 ? address[(at + 1)..].Trim() : null;
    }
}
