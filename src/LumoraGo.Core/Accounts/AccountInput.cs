using LumoraGo.Core.Mail;

namespace LumoraGo.Core.Accounts;

/// <summary>Ayarlar penceresindeki takvim ve mail alanlarını doğrulayıp <see cref="AccountSecrets"/>'e çevirir.</summary>
public static class AccountInput
{
    /// <summary>Her satıra bir ICS adresi. Boş ve "#" ile başlayan satırlar atlanır.</summary>
    public static IReadOnlyList<string> ParseCalendarUrls(string text, out IReadOnlyList<string> errors)
    {
        var urls = new List<string>();
        var problems = new List<string>();
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "webcal"))
            {
                problems.Add($"{i + 1}. satır bir takvim adresi değil: https:// ya da webcal:// ile başlamalı.");
            }
            else if (!urls.Contains(line, StringComparer.Ordinal))
            {
                urls.Add(line);
            }
        }

        errors = problems;
        return urls;
    }

    /// <param name="existing">Kayıtlı hesap; şifre kutusu boş bırakılırsa aynı adres için kayıtlı şifre korunur.</param>
    /// <returns>Adres boşsa (bağlantıyı kaldır) null hesap ve null hata; sorun varsa hata mesajı.</returns>
    public static (MailAccount? Account, string? Error) BuildMail(string address, string password, string server, MailAccount? existing)
    {
        address = address.Trim();
        if (address.Length == 0)
        {
            return (null, null);
        }

        if (!address.Contains('@') || address.EndsWith('@'))
        {
            return (null, "Mail adresi geçerli görünmüyor.");
        }

        if (MailProviders.IsMicrosoft(address))
        {
            return (null, "Outlook, Hotmail ve Microsoft 365 hesapları desteklenmiyor: Microsoft şifreyle IMAP girişini kapattı, yalnızca uygulama kaydı gerektiren OAuth çalışıyor. Gmail, Yahoo, Yandex, iCloud ya da başka bir IMAP sunucusu kullanabilirsin.");
        }

        if (password.Length == 0)
        {
            if (existing is not null && string.Equals(existing.Address, address, StringComparison.OrdinalIgnoreCase))
            {
                password = existing.Password;
            }
            else
            {
                return (null, "Mail şifresi (Gmail için uygulama şifresi) gerekli.");
            }
        }

        server = server.Trim();
        if (server.Length == 0)
        {
            return MailProviders.Find(address) is { } provider
                ? (new MailAccount(provider.Host, provider.Port, address, password), null)
                : (null, "Bu adresin sunucusunu tanımıyorum; IMAP sunucusunu yaz (örn. imap.example.com).");
        }

        var colon = server.LastIndexOf(':');
        var host = colon > 0 ? server[..colon] : server;
        var port = 993;
        if (colon > 0 && !(int.TryParse(server[(colon + 1)..], out port) && port is > 0 and < 65536))
        {
            return (null, "Sunucudaki port numarası geçerli değil (örn. imap.example.com:993).");
        }

        return host.Contains(' ') || host.Length == 0
            ? (null, "IMAP sunucu adı geçerli görünmüyor.")
            : (new MailAccount(host, port, address, password), null);
    }

    /// <summary>Kayıtlı sunucuyu ayarlar kutusuna yazılacak biçime çevirir; sağlayıcı zaten tahmin edilebiliyorsa boş bırakır.</summary>
    public static string ServerText(MailAccount account) =>
        MailProviders.Find(account.Address) is { } p && p.Host == account.Host && p.Port == account.Port
            ? ""
            : account.Port == 993 ? account.Host : $"{account.Host}:{account.Port}";
}
