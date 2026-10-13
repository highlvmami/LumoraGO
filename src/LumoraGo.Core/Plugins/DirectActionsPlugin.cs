using LumoraGo.Core.Actions;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Notes;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// Kutuya düz Türkçe yazılan işler: "hatırlat yarın 9 fatura", "mesaj ahmet geliyorum", "mail ayşe toplantı | yarın müsait misin",
/// "etkinlik cuma 14:00 dişçi", "not süt al". Ne anlaşıldığı satırda yazar, Enter onaylar. Mesaj ve mail hiçbir zaman
/// kendiliğinden gönderilmez; dolu bir taslak açılır.
/// </summary>
public sealed class DirectActionsPlugin(
    ReminderStore reminders,
    ContactBook contacts,
    NotesStore notes,
    Action<string> open,
    Func<DateTime>? now = null) : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.actions", "Doğrudan eylemler");

    public const int ActionScore = 3500;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim();
        var space = text.IndexOf(' ');
        var verb = FuzzyMatcher.Fold(space < 0 ? text : text[..space]);
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();

        Result? row = verb switch
        {
            "hatirlat" => Remind(rest),
            "mesaj" => Message(rest),
            "mail" or "eposta" or "e-posta" => Mail(rest),
            "etkinlik" => Event(rest),
            "not" when rest.Length > 0 => new Result($"Nota ekle: {rest}", "/not listesine eklenir · Enter", null, 120, _ =>
            {
                notes.Add(rest);
                _host?.ShowMessage("Not eklendi", rest);
                return true;
            }),
            _ => null,
        };

        return Task.FromResult<IReadOnlyList<Result>>(row is null ? [] : [row]);
    }

    private static Result Hint(string title, string subtitle, int score = ActionScore) => new(title, subtitle, null, score, _ => false);

    private Result Remind(string rest)
    {
        if (rest.Length == 0)
        {
            return Hint("Hatırlatıcı kur", "Örnek: hatırlat yarın 9 fatura · hatırlat 20 dk sonra çay · hatırlat cuma akşam annemi ara", 60);
        }

        var now = _now();
        if (TurkishDate.Parse(rest, now) is not { } date)
        {
            return Hint($"Hatırlat: {rest}", "Ne zaman? Zamanı başa yaz: \"hatırlat yarın 9 …\", \"hatırlat 2 saat sonra …\", \"hatırlat 3 kasım …\"");
        }

        if (date.Rest.Length == 0)
        {
            return Hint($"Hatırlat: {date.Describe(now)}", "Zamandan sonra neyi hatırlatayım? Örnek: hatırlat yarın 9 fatura");
        }

        var subtitle = date.Describe(now) + (date.Shifted ? " · bu saat bugün geçti, yarına kuruldu" : "") + " · Enter ile kur";
        return new Result($"Hatırlat: {date.Rest}", subtitle, null, ActionScore, _ =>
        {
            reminders.Add(date.Rest, date.When);
            _host?.ShowMessage("Hatırlatıcı kuruldu", $"{date.Rest} · {date.Describe(_now())}");
            return true;
        });
    }

    private Result Message(string rest)
    {
        if (rest.Length == 0)
        {
            return Hint("WhatsApp mesajı yaz", "Örnek: mesaj ahmet geliyorum · mesaj 0532 123 45 67 tamam", 60);
        }

        if (Recipient(rest, mail: false) is not { } to)
        {
            return Hint($"Mesaj: \"{FirstWord(rest)}\" kişilerde yok", contacts.All.Count == 0
                ? "Ayarlar → Kişiler'den rehberini aktar ya da numarayı yaz: mesaj 0532… mesajın"
                : "Adı kontrol et ya da numarayı yaz: mesaj 0532… mesajın");
        }

        var url = ContactsPlugin.WhatsApp(to.Target, to.Text);
        var preview = to.Text.Length > 0 ? $"“{to.Text}”" : "sohbet açılır";
        return new Result($"WhatsApp: {to.Name}", $"{preview} · {to.Target} · taslak açılır, göndermeyi sen yaparsın", null, ActionScore, _ =>
        {
            open(url);
            return true;
        });
    }

    private Result Mail(string rest)
    {
        if (rest.Length == 0)
        {
            return Hint("Mail yaz", "Örnek: mail ayşe toplantı | yarın 10'da müsait misin (| işaretinden sonrası mailin metni)", 60);
        }

        if (Recipient(rest, mail: true) is not { } to)
        {
            return Hint($"Mail: \"{FirstWord(rest)}\" kişilerde yok", "Adı kontrol et ya da adresi yaz: mail ad@ornek.com konu | metin");
        }

        var bar = to.Text.IndexOf('|');
        var subject = (bar < 0 ? to.Text : to.Text[..bar]).Trim();
        var body = bar < 0 ? "" : to.Text[(bar + 1)..].Trim();
        var query = new List<string>();
        if (subject.Length > 0)
        {
            query.Add("subject=" + Uri.EscapeDataString(subject));
        }

        if (body.Length > 0)
        {
            query.Add("body=" + Uri.EscapeDataString(body));
        }

        var url = "mailto:" + to.Target + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var subtitle = (subject.Length > 0 ? $"Konu: {subject}" : "Konu yok") + (body.Length > 0 ? $" · “{body}”" : "") + $" · {to.Target} · taslak açılır";
        return new Result($"Mail: {to.Name}", subtitle, null, ActionScore, _ =>
        {
            open(url);
            return true;
        });
    }

    private Result Event(string rest)
    {
        if (rest.Length == 0)
        {
            return Hint("Takvime etkinlik ekle", "Örnek: etkinlik cuma 14:00 dişçi · etkinlik 3 kasım doğum günü", 60);
        }

        var now = _now();
        if (TurkishDate.Parse(rest, now) is not { } date || date.Rest.Length == 0)
        {
            return Hint($"Etkinlik: {rest}", "Önce zamanı sonra adı yaz: etkinlik cuma 14:00 dişçi");
        }

        var url = GoogleCalendarUrl(date.Rest, date.When, date.HasTime);
        return new Result($"Etkinlik: {date.Rest}", $"{date.Describe(now)} · Google Takvim'de dolu açılır, kaydet sende", null, ActionScore, _ =>
        {
            open(url);
            return true;
        });
    }

    /// <summary>Saat varsa 1 saatlik, yoksa tüm gün etkinlik. Saat yerel yazılır; Google takvimin saat dilimini kullanır.</summary>
    public static string GoogleCalendarUrl(string title, DateTime when, bool hasTime)
    {
        var dates = hasTime
            ? $"{when:yyyyMMdd'T'HHmmss}/{when.AddHours(1):yyyyMMdd'T'HHmmss}"
            : $"{when:yyyyMMdd}/{when.AddDays(1):yyyyMMdd}";
        return "https://calendar.google.com/calendar/render?action=TEMPLATE&text=" + Uri.EscapeDataString(title) + "&dates=" + dates;
    }

    private sealed record To(string Name, string Target, string Text);

    /// <summary>Baştaki numarayı/adresi ya da en uzun eşleşen kişi adını (en çok 3 kelime) alıcı yapar, gerisi metin olur.</summary>
    private To? Recipient(string rest, bool mail)
    {
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (mail && words[0].Contains('@'))
        {
            return new To(words[0], words[0], string.Join(' ', words.Skip(1)));
        }

        if (!mail)
        {
            // "0532 123 45 67 tamam": baştaki rakam gruplarını numara say.
            var taken = 0;
            var digits = "";
            while (taken < words.Length && words[taken].TrimStart('+').All(c => char.IsAsciiDigit(c) || c is '(' or ')' or '-'))
            {
                digits += words[taken];
                taken++;
            }

            if (PhoneNumber.Digits(digits).Length >= 7)
            {
                return new To(digits, PhoneNumber.Normalize(digits), string.Join(' ', words.Skip(taken)));
            }
        }

        for (var n = Math.Min(3, words.Length); n >= 1; n--)
        {
            var parts = words.Take(n).Select(FuzzyMatcher.Fold).ToArray();
            var found = contacts.All
                .Where(c => mail ? c.Emails.Count > 0 : c.Phones.Count > 0)
                .Where(c =>
                {
                    var nameWords = FuzzyMatcher.Fold(c.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return parts.All(p => nameWords.Any(w => w.StartsWith(p, StringComparison.Ordinal)));
                })
                .OrderBy(c => c.Name.Length)
                .FirstOrDefault();
            if (found is not null)
            {
                return new To(found.Name, mail ? found.Emails[0] : found.Phones[0], string.Join(' ', words.Skip(n)));
            }
        }

        return null;
    }

    private static string FirstWord(string text) => text.Split(' ', 2)[0];
}

/// <summary>"/hatırlatıcılar": bekleyenler. Enter tamamlandı (siler), Ctrl+Enter 10 dakika erteler.</summary>
public sealed class RemindersPlugin(ReminderStore reminders, string keyword, Func<DateTime>? now = null) : IPlugin
{
    public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);

    public PluginMetadata Metadata { get; } = new("lumorago.reminders." + keyword.Trim('/'), "Hatırlatıcılar", keyword);

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var filter = FuzzyMatcher.Fold(query.Search.Trim());
        var now = _now();
        var items = reminders.All.Where(r => filter.Length == 0 || FuzzyMatcher.Fold(r.Text).Contains(filter, StringComparison.Ordinal)).ToList();
        if (items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([new Result(
                reminders.All.Count == 0 ? "Bekleyen hatırlatıcı yok" : "Eşleşen hatırlatıcı yok",
                "Kurmak için: hatırlat yarın 9 fatura", null, 1, _ => false)]);
        }

        return Task.FromResult<IReadOnlyList<Result>>(items.Select((r, i) =>
        {
            var when = new DateMatch(r.Due, true, false, "").Describe(now);
            var late = r.Due <= now ? " · vakti geldi" : "";
            return new Result(r.Text, $"{when}{late} · Enter tamamlandı · Ctrl+Enter 10 dk ertele", null, 1000 - i, ctx =>
            {
                if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
                {
                    reminders.Snooze(r.Id, _now() + SnoozeFor);
                }
                else
                {
                    reminders.Remove(r.Id);
                }

                return true;
            },
            [
                new ContextAction("Tamamlandı (sil)", _ =>
                {
                    reminders.Remove(r.Id);
                    return true;
                }),
                new ContextAction("10 dakika ertele", _ =>
                {
                    reminders.Snooze(r.Id, _now() + SnoozeFor);
                    return true;
                }),
                new ContextAction("Yarın aynı saate ertele", _ =>
                {
                    reminders.Snooze(r.Id, (r.Due > _now() ? r.Due : _now()).AddDays(1));
                    return true;
                }),
            ]);
        }).ToList());
    }
}
