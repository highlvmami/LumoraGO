using LumoraGo.Core.Actions;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Notes;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class TurkishDateTests
{
    // Salı, 13 Ekim 2026 15:30
    private static readonly DateTime Now = new(2026, 10, 13, 15, 30, 0);

    [Theory]
    [InlineData("yarın 9 fatura", "2026-10-14 09:00", true, "fatura")]
    [InlineData("yarın fatura", "2026-10-14 09:00", false, "fatura")]
    [InlineData("bugün 18:45 toplantı", "2026-10-13 18:45", true, "toplantı")]
    [InlineData("öbür gün sabah koşu", "2026-10-15 09:00", true, "koşu")]
    [InlineData("cuma 14:00 dişçi", "2026-10-16 14:00", true, "dişçi")]
    [InlineData("Cuma 14.30 dişçi", "2026-10-16 14:30", true, "dişçi")]
    [InlineData("pazartesi akşam annemi ara", "2026-10-19 19:00", true, "annemi ara")]
    [InlineData("akşam 7 film", "2026-10-13 19:00", true, "film")]
    [InlineData("haftaya salı rapor", "2026-10-20 09:00", false, "rapor")]
    [InlineData("haftaya cuma rapor", "2026-10-23 09:00", false, "rapor")]
    [InlineData("3 kasım doğum günü", "2026-11-03 09:00", false, "doğum günü")]
    [InlineData("1 ocak yılbaşı", "2027-01-01 09:00", false, "yılbaşı")]
    [InlineData("20 dk sonra çay", "2026-10-13 15:50", true, "çay")]
    [InlineData("2 saat sonra mola", "2026-10-13 17:30", true, "mola")]
    [InlineData("3 gün sonra kargo", "2026-10-16 15:30", true, "kargo")]
    [InlineData("saat 17 servis", "2026-10-13 17:00", true, "servis")]
    [InlineData("yarın 9'da fatura", "2026-10-14 09:00", true, "fatura")]
    [InlineData("salı 16:00 x", "2026-10-13 16:00", true, "x")]
    public void Parses_day_time_and_rest(string text, string expected, bool hasTime, string rest)
    {
        var match = TurkishDate.Parse(text, Now)!;

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), match.When);
        Assert.Equal(hasTime, match.HasTime);
        Assert.Equal(rest, match.Rest);
        Assert.False(match.Shifted);
    }

    [Fact]
    public void Past_time_today_moves_to_tomorrow()
    {
        var match = TurkishDate.Parse("10:00 spor", Now)!;

        Assert.Equal(new DateTime(2026, 10, 14, 10, 0, 0), match.When);
        Assert.True(match.Shifted);
    }

    [Fact]
    public void Same_weekday_with_past_time_goes_to_next_week()
    {
        var match = TurkishDate.Parse("salı 9 x", Now)!;
        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0), match.When);
        Assert.False(match.Shifted);
    }

    [Theory]
    [InlineData("fatura öde")]
    [InlineData("3 elma al")]
    [InlineData("")]
    public void Text_without_date_is_null(string text) => Assert.Null(TurkishDate.Parse(text, Now));

    [Fact]
    public void Describes_in_turkish()
    {
        Assert.Equal("Yarın 14 Ekim Çarşamba 09:00", TurkishDate.Parse("yarın 9 x", Now)!.Describe(Now));
        Assert.Equal("3 Kasım Salı", TurkishDate.Parse("3 kasım x", Now)!.Describe(Now));
    }
}

public sealed class ReminderStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "lumorago-rem-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Due_reminders_are_given_once_and_survive_restart()
    {
        var store = new ReminderStore(_path);
        var at = new DateTime(2026, 10, 14, 9, 0, 0);
        store.Add("fatura", at);
        store.Add("çay", at.AddHours(1));

        Assert.Empty(store.TakeDue(at.AddMinutes(-1)));
        Assert.Equal(["fatura"], store.TakeDue(at).Select(r => r.Text));
        Assert.Empty(store.TakeDue(at.AddMinutes(1)));

        // Kapalıyken kaçan hatırlatıcı açılışta gelir.
        var reopened = new ReminderStore(_path);
        Assert.Equal(["çay"], reopened.TakeDue(at.AddHours(5)).Select(r => r.Text));
        Assert.Equal(2, reopened.All.Count);
    }

    [Fact]
    public void Snooze_rearms_and_remove_deletes()
    {
        var store = new ReminderStore(null);
        var at = new DateTime(2026, 10, 14, 9, 0, 0);
        var reminder = store.Add("x", at);
        store.TakeDue(at);

        store.Snooze(reminder.Id, at.AddMinutes(10));
        Assert.Empty(store.TakeDue(at.AddMinutes(5)));
        Assert.Single(store.TakeDue(at.AddMinutes(10)));

        store.Remove(reminder.Id);
        Assert.Empty(store.All);
    }
}

public sealed class DirectActionsPluginTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 15, 30, 0);

    private readonly ReminderStore _reminders = new(null);
    private readonly NotesStore _notes = new(null);
    private readonly List<string> _opened = [];
    private readonly NullHost _host = new();
    private readonly ContactBook _book;

    public DirectActionsPluginTests()
    {
        _book = new ContactBook(Path.Combine(Path.GetTempPath(), "lumorago-da-" + Guid.NewGuid().ToString("N"), "c.dat"), b => b, b => b);
        _book.Save([
            new Contact("Ahmet Kaya", ["0532 123 45 67"], ["ahmet@example.com"]),
            new Contact("Ahmet Yılmaz", ["0505 000 00 00"], []),
            new Contact("Ayşe Demir", [], ["ayse@example.com"]),
        ]);
    }

    private async Task<Result?> Ask(string text)
    {
        var plugin = new DirectActionsPlugin(_reminders, _book, _notes, _opened.Add, () => Now);
        plugin.Init(new PluginContext(DirectActionsPlugin.Metadata, "", _host));
        return (await plugin.QueryAsync(new Query(text, null, text), CancellationToken.None)).SingleOrDefault();
    }

    [Fact]
    public async Task Remind_shows_what_it_understood_and_adds_on_enter()
    {
        var row = (await Ask("hatırlat yarın 9 fatura öde"))!;

        Assert.Equal("Hatırlat: fatura öde", row.Title);
        Assert.StartsWith("Yarın 14 Ekim Çarşamba 09:00", row.Subtitle);
        Assert.Equal(DirectActionsPlugin.ActionScore, row.Score);

        row.Action(new ActionContext(ActionModifiers.None));
        var reminder = Assert.Single(_reminders.All);
        Assert.Equal(("fatura öde", new DateTime(2026, 10, 14, 9, 0, 0)), (reminder.Text, reminder.Due));
        Assert.Equal("Hatırlatıcı kuruldu", _host.Messages.Single().Title);
    }

    [Fact]
    public async Task Remind_without_time_asks_for_it()
    {
        var row = (await Ask("hatirlat fatura"))!;

        Assert.False(row.Action(new ActionContext(ActionModifiers.None)));
        Assert.Contains("Ne zaman?", row.Subtitle);
        Assert.Empty(_reminders.All);
    }

    [Fact]
    public async Task Remind_says_when_past_time_moved_to_tomorrow()
    {
        var row = (await Ask("hatırlat 10:00 spor"))!;
        Assert.Contains("yarına kuruldu", row.Subtitle);
    }

    [Fact]
    public async Task Message_finds_contact_by_full_name_and_opens_whatsapp_draft()
    {
        var row = (await Ask("mesaj ahmet yılmaz geliyorum"))!;

        Assert.Equal("WhatsApp: Ahmet Yılmaz", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://wa.me/905050000000?text=geliyorum"], _opened);
    }

    [Fact]
    public async Task Message_with_first_name_only_picks_a_match_and_keeps_text()
    {
        var row = (await Ask("mesaj ahmet 5 dk geç kalırım"))!;

        Assert.StartsWith("WhatsApp: Ahmet", row.Title);
        Assert.Contains("“5 dk geç kalırım”", row.Subtitle);
    }

    [Fact]
    public async Task Message_to_a_number()
    {
        var row = (await Ask("mesaj 0532 999 88 77 tamam"))!;

        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://wa.me/905329998877?text=tamam"], _opened);
    }

    [Fact]
    public async Task Message_to_unknown_name_says_so()
    {
        var row = (await Ask("mesaj zeynep selam"))!;

        Assert.Equal("Mesaj: \"zeynep\" kişilerde yok", row.Title);
        Assert.False(row.Action(new ActionContext(ActionModifiers.None)));
    }

    [Fact]
    public async Task Mail_splits_subject_and_body()
    {
        var row = (await Ask("mail ayşe toplantı | yarın 10'da müsait misin"))!;

        Assert.Equal("Mail: Ayşe Demir", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["mailto:ayse@example.com?subject=toplant%C4%B1&body=yar%C4%B1n%2010%27da%20m%C3%BCsait%20misin"], _opened);
    }

    [Fact]
    public async Task Mail_to_an_address()
    {
        (await Ask("mail biri@ornek.com selam"))!.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["mailto:biri@ornek.com?subject=selam"], _opened);
    }

    [Fact]
    public async Task Event_opens_google_calendar_template()
    {
        var row = (await Ask("etkinlik cuma 14:00 dişçi"))!;

        Assert.Equal("Etkinlik: dişçi", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://calendar.google.com/calendar/render?action=TEMPLATE&text=di%C5%9F%C3%A7i&dates=20261016T140000/20261016T150000"], _opened);
    }

    [Fact]
    public void All_day_event_url() =>
        Assert.EndsWith("&dates=20261103/20261104", DirectActionsPlugin.GoogleCalendarUrl("x", new DateTime(2026, 11, 3, 9, 0, 0), false));

    [Fact]
    public async Task Note_is_added_with_low_score()
    {
        var row = (await Ask("not süt al"))!;

        Assert.True(row.Score < 200);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal("süt al", Assert.Single(_notes.Notes).Text);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("mesajlar")]
    [InlineData("chrome")]
    [InlineData("not")]
    public async Task Other_text_is_ignored(string text) => Assert.Null(await Ask(text));
}

public sealed class RemindersPluginTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 15, 30, 0);

    [Fact]
    public async Task Lists_pending_with_done_and_snooze()
    {
        var store = new ReminderStore(null);
        store.Add("fatura", Now.AddDays(1));
        var tea = store.Add("çay", Now.AddMinutes(-5));
        var plugin = new RemindersPlugin(store, "/hatırlatıcılar", () => Now);

        var rows = await plugin.QueryAsync(new Query("/hatırlatıcılar ", "/hatırlatıcılar", ""), CancellationToken.None);
        Assert.Equal(["çay", "fatura"], rows.Select(r => r.Title));
        Assert.Contains("vakti geldi", rows[0].Subtitle);

        rows[0].Action(new ActionContext(ActionModifiers.Ctrl));
        Assert.Equal(Now + RemindersPlugin.SnoozeFor, store.All.Single(r => r.Id == tea.Id).Due);

        rows[1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["çay"], store.All.Select(r => r.Text));
        Assert.Equal("lumorago.reminders.hatırlatıcılar", plugin.Metadata.Id);
    }

    [Fact]
    public async Task Empty_list_explains_how_to_add()
    {
        var plugin = new RemindersPlugin(new ReminderStore(null), "/hatirlaticilar", () => Now);
        var row = Assert.Single(await plugin.QueryAsync(new Query("/hatirlaticilar", "/hatirlaticilar", ""), CancellationToken.None));
        Assert.Equal("Bekleyen hatırlatıcı yok", row.Title);
    }
}
