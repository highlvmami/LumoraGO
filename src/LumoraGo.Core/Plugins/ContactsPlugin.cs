using LumoraGo.Core.Contacts;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// İçe aktarılan rehberde arar ("/kisi ahmet", ayar açıksa doğrudan "ahmet"). Enter numarayı (yoksa adresi) kopyalar,
/// Ctrl+Enter WhatsApp sohbetini, Shift+Enter yeni maili açar; sağ ok menüsünde arama ve diğer numaralar.
/// </summary>
public sealed class ContactsPlugin : IPlugin
{
    public const string Keyword = "/kisi";
    public static readonly PluginMetadata GlobalMetadata = new("lumorago.contacts", "Kişiler");

    private const int MaxResults = 20;

    private readonly ContactBook _book;
    private readonly Action<string> _open;
    private readonly Func<bool> _inGlobalSearch;
    private readonly Action? _openSettings;
    private IPluginHost? _host;

    /// <param name="keyword">Verilirse yalnızca bu kelimeyle çalışır ve boş yazınca herkesi listeler.</param>
    public ContactsPlugin(ContactBook book, Action<string> open, Func<bool> inGlobalSearch, Action? openSettings = null, string? keyword = null)
    {
        _book = book;
        _open = open;
        _inGlobalSearch = inGlobalSearch;
        _openSettings = openSettings;
        Metadata = keyword is null
            ? GlobalMetadata
            : new PluginMetadata(keyword == Keyword ? "lumorago.contacts.kw" : "lumorago.contacts.kw." + keyword.Trim('/'), "Kişiler", keyword);
    }

    public PluginMetadata Metadata { get; }

    private bool IsKeyword => Metadata.ActionKeyword is not null;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim();
        if (!IsKeyword && (text.Length < 2 || !_inGlobalSearch()))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        if (IsKeyword && _book.All.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([new Result("Rehber boş", "Ayarlar → Kişiler'den Google Kişiler ya da iPhone'dan aldığın .vcf/.csv dosyasını seç", null, 1, _ =>
            {
                _openSettings?.Invoke();
                return true;
            })]);
        }

        var results = _book.Search(text)
            .Take(MaxResults)
            .Select(m => Row(m.Contact, IsKeyword ? m.Score : m.Score / 2))
            .ToList();
        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private Result Row(Contact contact, int score)
    {
        var menu = new List<ContextAction>();
        foreach (var phone in contact.Phones)
        {
            var normalized = PhoneNumber.Normalize(phone);
            menu.Add(new ContextAction($"Ara: {phone}", _ => Open("tel:" + normalized)));
            menu.Add(new ContextAction($"WhatsApp: {phone}", _ => Open(WhatsApp(phone))));
            menu.Add(new ContextAction($"Kopyala: {phone}", _ => Copy(phone)));
        }

        foreach (var email in contact.Emails)
        {
            menu.Add(new ContextAction($"Mail at: {email}", _ => Open("mailto:" + email)));
            menu.Add(new ContextAction($"Kopyala: {email}", _ => Copy(email)));
        }

        return new Result(contact.Name, contact.Summary(), null, score, ctx => Act(contact, ctx.Modifiers), menu)
        {
            PreviewText = contact.FullText(),
            DragData = DragPayload.Text(contact.Phones.FirstOrDefault() ?? contact.Emails.FirstOrDefault() ?? contact.Name),
        };
    }

    private bool Act(Contact contact, ActionModifiers modifiers)
    {
        var phone = contact.Phones.FirstOrDefault();
        var email = contact.Emails.FirstOrDefault();
        if (modifiers.HasFlag(ActionModifiers.Ctrl) && phone is not null)
        {
            return Open(WhatsApp(phone));
        }

        if (modifiers.HasFlag(ActionModifiers.Shift) && email is not null)
        {
            return Open("mailto:" + email);
        }

        return Copy(phone ?? email ?? contact.Name);
    }

    public static string WhatsApp(string phone, string? text = null) =>
        $"https://wa.me/{PhoneNumber.WhatsAppDigits(phone)}" + (string.IsNullOrEmpty(text) ? "" : "?text=" + Uri.EscapeDataString(text));

    private bool Open(string target)
    {
        _open(target);
        return true;
    }

    private bool Copy(string text)
    {
        _host?.CopyToClipboard(text);
        return true;
    }
}
