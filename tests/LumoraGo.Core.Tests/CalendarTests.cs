using LumoraGo.Core.Calendar;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class CalendarTests
{
    // Sınamalar işletim sisteminin saat dilimi verisine bağlı kalmasın: sabit UTC+3 bölge.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test", "test");

    private static readonly DateTime Day = new(2026, 10, 13, 0, 0, 0);

    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n"
        + "BEGIN:VTIMEZONE\r\nTZID:Turkey Standard Time\r\nBEGIN:STANDARD\r\nDTSTART:16010101T000000\r\nTZOFFSETFROM:+0300\r\nTZOFFSETTO:+0300\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n"
        + string.Concat(events.Select(e => $"BEGIN:VEVENT\r\nUID:{Guid.NewGuid()}@test\r\n{e}\r\nEND:VEVENT\r\n"))
        + "END:VCALENDAR\r\n";

    private static CalendarSource Source(string ics = "", Func<DateTime>? now = null, TimeSpan? cache = null, FakeHandler? handler = null, params string[] urls) =>
        new(new HttpClient(handler ?? new FakeHandler(_ => new HttpResponseMessage { Content = new StringContent(ics) })),
            () => urls.Length == 0 ? ["https://example.com/cal.ics"] : urls, Zone, now, cache);

    private static IReadOnlyList<CalendarItem> Parse(string ics, DateTime? from = null, DateTime? to = null) =>
        Source().Parse(ics, from ?? Day, to ?? Day.AddDays(1));

    [Fact]
    public void Utc_event_is_converted_to_local_time()
    {
        var item = Assert.Single(Parse(Ics("DTSTART:20261013T070000Z\r\nDTEND:20261013T080000Z\r\nSUMMARY:Sprint")));
        Assert.Equal("Sprint", item.Title);
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), item.Start);
        Assert.Equal(new DateTime(2026, 10, 13, 11, 0, 0), item.End);
        Assert.False(item.AllDay);
    }

    [Fact]
    public void Iana_and_windows_time_zone_ids_are_understood()
    {
        var items = Parse(Ics(
            "DTSTART;TZID=Europe/Istanbul:20261013T100000\r\nDTEND;TZID=Europe/Istanbul:20261013T110000\r\nSUMMARY:Google",
            "DTSTART;TZID=Turkey Standard Time:20261013T140000\r\nDTEND;TZID=Turkey Standard Time:20261013T150000\r\nSUMMARY:Outlook"));
        Assert.Equal(["Google", "Outlook"], items.OrderBy(i => i.Start).Select(i => i.Title));
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), items.Single(i => i.Title == "Google").Start);
        Assert.Equal(new DateTime(2026, 10, 13, 14, 0, 0), items.Single(i => i.Title == "Outlook").Start);
    }

    [Fact]
    public void All_day_and_floating_events_keep_their_written_time()
    {
        var items = Parse(Ics(
            "DTSTART;VALUE=DATE:20261013\r\nDTEND;VALUE=DATE:20261014\r\nSUMMARY:Tatil",
            "DTSTART:20261013T180000\r\nDTEND:20261013T190000\r\nSUMMARY:Kayan"));
        var allDay = items.Single(i => i.Title == "Tatil");
        Assert.True(allDay.AllDay);
        Assert.Equal(Day, allDay.Start);
        Assert.Equal(Day.AddDays(1), allDay.End);
        Assert.Equal(new DateTime(2026, 10, 13, 18, 0, 0), items.Single(i => i.Title == "Kayan").Start);
    }

    [Fact]
    public void Cancelled_events_are_skipped()
    {
        var items = Parse(Ics(
            "DTSTART:20261013T070000Z\r\nDTEND:20261013T080000Z\r\nSUMMARY:İptal\r\nSTATUS:CANCELLED",
            "DTSTART:20261013T090000Z\r\nDTEND:20261013T100000Z\r\nSUMMARY:Kalan"));
        Assert.Equal("Kalan", Assert.Single(items).Title);
    }

    [Fact]
    public void Recurring_events_expand_only_on_matching_days()
    {
        var ics = Ics("DTSTART;TZID=Europe/Istanbul:20250101T100000\r\nDTEND;TZID=Europe/Istanbul:20250101T103000\r\nRRULE:FREQ=WEEKLY;BYDAY=TU,TH\r\nSUMMARY:Daily");
        var source = Source();
        Assert.Single(source.Parse(ics, Day, Day.AddDays(1)));               // Salı
        Assert.Empty(source.Parse(ics, Day.AddDays(1), Day.AddDays(2)));     // Çarşamba
        Assert.Single(source.Parse(ics, Day.AddDays(2), Day.AddDays(3)));    // Perşembe
        Assert.Equal(2, source.Parse(ics, Day, Day.AddDays(7)).Count);
    }

    [Fact]
    public void Events_outside_the_range_are_dropped_and_running_ones_kept()
    {
        var items = Parse(Ics(
            "DTSTART:20261012T070000Z\r\nDTEND:20261012T080000Z\r\nSUMMARY:Dün",
            "DTSTART:20261014T070000Z\r\nDTEND:20261014T080000Z\r\nSUMMARY:Yarın",
            "DTSTART:20261012T210000Z\r\nDTEND:20261013T030000Z\r\nSUMMARY:Gece boyu"));
        Assert.Equal("Gece boyu", Assert.Single(items).Title);
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData("Katıl: https://teams.microsoft.com/l/meetup-join/19%3ameeting_x/0?context=1.", "https://teams.microsoft.com/l/meetup-join/19%3ameeting_x/0?context=1")]
    [InlineData("https://us02web.zoom.us/j/123456?pwd=abc)", "https://us02web.zoom.us/j/123456?pwd=abc")]
    public void Finds_meeting_links(string text, string expected) =>
        Assert.Equal(expected, CalendarSource.FindMeetingUrl(null, text));

    [Fact]
    public void Meeting_link_is_taken_from_description_or_location()
    {
        var items = Parse(Ics(
            "DTSTART:20261013T070000Z\r\nDTEND:20261013T080000Z\r\nSUMMARY:A\r\nLOCATION:Oda 4\r\nDESCRIPTION:Bağlantı https://zoom.us/j/99",
            "DTSTART:20261013T090000Z\r\nDTEND:20261013T100000Z\r\nSUMMARY:B\r\nLOCATION:Oda 5"));
        Assert.Equal("https://zoom.us/j/99", items.Single(i => i.Title == "A").MeetingUrl);
        Assert.Null(items.Single(i => i.Title == "B").MeetingUrl);
        Assert.Equal("Oda 5", items.Single(i => i.Title == "B").Location);
    }

    [Fact]
    public async Task Downloads_once_within_cache_window_and_refetches_after()
    {
        var now = new DateTime(2026, 10, 13, 6, 0, 0, DateTimeKind.Utc);
        var ics = Ics("DTSTART:20261013T070000Z\r\nDTEND:20261013T080000Z\r\nSUMMARY:Sprint");
        var handler = new FakeHandler(_ => new HttpResponseMessage { Content = new StringContent(ics) });
        var source = Source(now: () => now, cache: TimeSpan.FromMinutes(5), handler: handler, urls: "webcal://example.com/c.ics");

        Assert.Single(await source.GetAsync(Day, Day.AddDays(1), null, CancellationToken.None));
        now = now.AddMinutes(4);
        await source.GetAsync(Day, Day.AddDays(1), null, CancellationToken.None);
        Assert.Single(handler.Requests);
        Assert.Equal("https://example.com/c.ics", handler.Requests[0]);

        now = now.AddMinutes(2);
        await source.GetAsync(Day, Day.AddDays(1), null, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Bad_calendars_are_reported_without_leaking_the_secret_address()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.Contains("gone")
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage { Content = new StringContent("<html>giriş yap</html>") });
        var source = Source(handler: handler, urls: ["https://calendar.example.com/gone/secret-token/basic.ics", "https://other.example.org/page"]);
        var errors = new List<string>();

        var items = await source.GetAsync(Day, Day.AddDays(1), errors.Add, CancellationToken.None);

        Assert.Empty(items);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("calendar.example.com: ", errors[0]);
        Assert.Contains("404", errors[0]);
        Assert.StartsWith("other.example.org: ", errors[1]);
        Assert.DoesNotContain(errors, e => e.Contains("secret-token"));
    }

    // ---- /takvim eklentisi ----

    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0);

    private static string Day13 =>
        Ics(
            "DTSTART:20261013T060000Z\r\nDTEND:20261013T070000Z\r\nSUMMARY:Bitti",                                  // 09-10 yerel
            "DTSTART:20261013T083000Z\r\nDTEND:20261013T093000Z\r\nSUMMARY:Süren",                                  // 11:30-12:30
            "DTSTART:20261013T110000Z\r\nDTEND:20261013T120000Z\r\nSUMMARY:Planlama\r\nLOCATION:https://meet.google.com/xyz", // 14-15
            "DTSTART:20261013T130000Z\r\nDTEND:20261013T140000Z\r\nSUMMARY:Akşam\r\nLOCATION:Cafe",                // 16-17
            "DTSTART:20261014T070000Z\r\nDTEND:20261014T080000Z\r\nSUMMARY:Yarınki");

    private static async Task<(CalendarPlugin Plugin, NullHost Host)> Ready(string ics, Action<string>? open = null, Action? settings = null)
    {
        var host = new NullHost();
        var plugin = new CalendarPlugin(Source(ics, now: () => Now.ToUniversalTime()), open ?? (_ => { }), settings ?? (() => { }), () => Now);
        plugin.Init(new PluginContext(CalendarPlugin.Metadata, "", host));
        var first = Assert.Single(await plugin.QueryAsync(new Query("/takvim", "/takvim", ""), CancellationToken.None));
        Assert.Equal("Takvim yükleniyor…", first.Title);
        await Wait.Until(() => host.Refreshes.Contains("/takvim"));
        return (plugin, host);
    }

    private static async Task<IReadOnlyList<Result>> Ask(CalendarPlugin plugin, string search) =>
        await plugin.QueryAsync(new Query($"/takvim {search}".Trim(), "/takvim", search), CancellationToken.None);

    [Fact]
    public async Task Unconfigured_plugin_points_to_settings()
    {
        var opened = 0;
        var plugin = new CalendarPlugin(new CalendarSource(new HttpClient(), () => []), _ => { }, () => opened++, () => Now);
        plugin.Init(new PluginContext(CalendarPlugin.Metadata, "", new NullHost()));

        var row = Assert.Single(await Ask(plugin, ""));
        Assert.Equal("Takvim bağlı değil", row.Title);
        Assert.True(row.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Today_shows_running_and_upcoming_but_not_finished_events()
    {
        var (plugin, _) = await Ready(Day13);
        var titles = (await Ask(plugin, "bugün")).Select(r => r.Title).ToList();

        Assert.Equal(["şimdi–12:30 · Süren", "14:00–15:00 · Planlama", "16:00–17:00 · Akşam"], titles);
    }

    [Fact]
    public async Task Default_view_shows_this_week_and_the_next_month_with_dates()
    {
        var (plugin, _) = await Ready(Ics(
            "DTSTART:20261013T110000Z\r\nDTEND:20261013T120000Z\r\nSUMMARY:Planlama",
            "DTSTART:20261014T070000Z\r\nDTEND:20261014T080000Z\r\nSUMMARY:Yarınki",
            "DTSTART:20261016T070000Z\r\nDTEND:20261016T080000Z\r\nSUMMARY:Cuma",
            "DTSTART:20261112T070000Z\r\nDTEND:20261112T080000Z\r\nSUMMARY:Ay içinde",
            "DTSTART:20261120T070000Z\r\nDTEND:20261120T080000Z\r\nSUMMARY:Çok ileride"));
        var titles = (await Ask(plugin, "")).Select(r => r.Title).ToList();

        Assert.Equal(["Bugün 14:00–15:00 · Planlama", "Yarın 10:00–11:00 · Yarınki", "Cum 16 Eki 10:00–11:00 · Cuma", "Per 12 Kas 10:00–11:00 · Ay içinde"], titles);
        Assert.Equal(titles, (await Ask(plugin, "ay")).Select(r => r.Title));
    }

    [Fact]
    public async Task Enter_joins_the_meeting_or_copies_the_title()
    {
        var opened = new List<string>();
        var (plugin, host) = await Ready(Day13, opened.Add);
        var rows = await Ask(plugin, "");

        Assert.Contains("toplantıya katıl", rows[1].Subtitle);
        rows[1].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["https://meet.google.com/xyz"], opened);

        Assert.StartsWith("Cafe", rows[2].Subtitle);
        rows[2].Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["Akşam"], host.Copied);
    }

    [Fact]
    public async Task Tomorrow_and_week_views()
    {
        var (plugin, _) = await Ready(Day13);

        Assert.Equal(["10:00–11:00 · Yarınki"], (await Ask(plugin, "yarın")).Select(r => r.Title));
        var week = (await Ask(plugin, "hafta")).Select(r => r.Title).ToList();
        Assert.Equal(5, week.Count);
        Assert.StartsWith("Sal ", week[0]);
        Assert.StartsWith("Çar ", week[^1]);
        Assert.Equal("Anlaşılmadı", (await Ask(plugin, "xyz"))[0].Title);
    }

    [Fact]
    public async Task Empty_day_says_so()
    {
        var (plugin, _) = await Ready(Ics("DTSTART:20261014T070000Z\r\nDTEND:20261014T080000Z\r\nSUMMARY:Yarınki"));
        Assert.Equal("Bugün başka etkinlik yok", Assert.Single(await Ask(plugin, "bugün")).Title);
    }

    [Fact]
    public async Task Unreachable_calendar_shows_an_error_row_that_opens_settings()
    {
        var opened = 0;
        var host = new NullHost();
        var source = Source(handler: new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)));
        var plugin = new CalendarPlugin(source, _ => { }, () => opened++, () => Now);
        plugin.Init(new PluginContext(CalendarPlugin.Metadata, "", host));
        await Ask(plugin, "");
        await Wait.Until(() => host.Refreshes.Contains("/takvim"));

        var rows = await Ask(plugin, "");
        var error = rows.Single(r => r.Title.StartsWith("Takvim okunamadı"));
        Assert.Contains("example.com", error.Title);
        error.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(1, opened);
    }
}

/// <summary>Arka plan işlerinin bitmesini en çok 10 sn bekler.</summary>
internal static class Wait
{
    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Koşul zamanında gerçekleşmedi.");
            }

            await Task.Delay(10);
        }
    }
}
