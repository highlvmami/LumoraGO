using System.Text;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0532 123 45 67", "+905321234567")]
    [InlineData("+90 (532) 123-45-67", "+905321234567")]
    [InlineData("532 123 4567", "+905321234567")]
    [InlineData("905321234567", "+905321234567")]
    [InlineData("0090 532 123 45 67", "+905321234567")]
    [InlineData("0212 555 00 11", "+902125550011")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    [InlineData("112", "112")]
    public void Normalizes_turkish_numbers(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(raw));

    [Fact]
    public void WhatsApp_link_has_digits_only() =>
        Assert.Equal("https://wa.me/905321234567?text=geliyorum%20%C5%9Fimdi", ContactsPlugin.WhatsApp("0532 123 45 67", "geliyorum şimdi"));
}

public sealed class ContactImportTests
{
    [Fact]
    public void Reads_vcard_with_folding_groups_and_escapes()
    {
        const string Vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Yılmaz;Özlem;;;\r\nFN:Özlem Yılmaz\r\nORG:Lumora\\, A.Ş.;Yazılım\r\n"
            + "TEL;TYPE=CELL:+90 532 123 45 67\r\nitem1.TEL:0212 555\r\n  00 11\r\nEMAIL;TYPE=INTERNET:ozlem@example.com\r\nEND:VCARD\r\n"
            + "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Kaya;Ahmet;;Dr.;\r\nTEL:05051112233\r\nEND:VCARD\r\n"
            + "BEGIN:VCARD\r\nFN:Adı var numarası yok\r\nEND:VCARD\r\n";

        var contacts = ContactImport.ParseVCard(Vcf);

        Assert.Equal(2, contacts.Count);
        Assert.Equal("Özlem Yılmaz", contacts[0].Name);
        Assert.Equal(["+90 532 123 45 67", "0212 555 00 11"], contacts[0].Phones);
        Assert.Equal(["ozlem@example.com"], contacts[0].Emails);
        Assert.Equal("Lumora, A.Ş. Yazılım", contacts[0].Organization);
        Assert.Equal("Dr. Ahmet Kaya", contacts[1].Name);
    }

    [Fact]
    public void Reads_old_quoted_printable_vcards()
    {
        const string Vcf = "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:;=C5=9E=C3=BCkr=C3=BC;;;\n"
            + "FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:=C5=9E=C3=BCkr=C3=BC =C3=87=\n=C4=B1nar\nTEL;CELL:05551234567\nEND:VCARD\n";

        var contact = Assert.Single(ContactImport.ParseVCard(Vcf));
        Assert.Equal("Şükrü Çınar", contact.Name);
    }

    [Fact]
    public void Reads_google_csv_with_multiple_values()
    {
        const string Csv = "﻿First Name,Middle Name,Last Name,Organization Name,E-mail 1 - Label,E-mail 1 - Value,Phone 1 - Label,Phone 1 - Value,Phone 2 - Label,Phone 2 - Value\n"
            + "Ayşe,,Demir,\"Okul, Lise\",* Home,ayse@example.com,Mobile,0532 111 22 33 ::: 0533 444 55 66,Work,0212 000 00 00\n"
            + ",,,,,,,,,\n"
            + "Mehmet,,,,,,Mobile,05050000000,,\n";

        var contacts = ContactImport.Parse(Csv, "contacts.csv");

        Assert.Equal(["Ayşe Demir", "Mehmet"], contacts.Select(c => c.Name));
        Assert.Equal(["0532 111 22 33", "0533 444 55 66", "0212 000 00 00"], contacts[0].Phones);
        Assert.Equal(["ayse@example.com"], contacts[0].Emails);
        Assert.Equal("Okul, Lise", contacts[0].Organization);
    }

    [Fact]
    public void Reads_outlook_csv()
    {
        const string Csv = "First Name,Last Name,Company,E-mail Address,E-mail Display Name,Mobile Phone,Business Phone\n"
            + "Can,Er,ACME,can@acme.com,Can Er (can@acme.com),+90 555 000 11 22,\n";

        var contact = Assert.Single(ContactImport.ParseCsv(Csv));
        Assert.Equal("Can Er", contact.Name);
        Assert.Equal(["can@acme.com"], contact.Emails);
        Assert.Equal(["+90 555 000 11 22"], contact.Phones);
    }

    [Fact]
    public void Decodes_windows_1254_csv()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1254).GetBytes("Name,Mobile Phone\nŞule Işık,05321112233\n");

        Assert.StartsWith("Name,Mobile Phone\nŞule Işık", ContactBook.DecodeText(bytes));
    }
}

public sealed class ContactBookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-contacts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static byte[] Flip(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();

    private ContactBook Book() => new(Path.Combine(_dir, "contacts.dat"), Flip, Flip);

    private string File(string name, string text)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Import_saves_protected_and_reloads()
    {
        var book = Book();
        var changed = 0;
        book.Changed += (_, _) => changed++;

        var count = book.Import(File("k.vcf", "BEGIN:VCARD\nFN:Özlem Yılmaz\nTEL:05321234567\nEND:VCARD\n"));

        Assert.Equal(1, count);
        Assert.Equal(1, changed);
        Assert.DoesNotContain("Yılmaz", System.IO.File.ReadAllText(Path.Combine(_dir, "contacts.dat")));
        Assert.Equal("Özlem Yılmaz", Assert.Single(Book().All).Name);
    }

    [Fact]
    public void Import_of_file_without_contacts_keeps_old_ones()
    {
        var book = Book();
        book.Import(File("k.vcf", "BEGIN:VCARD\nFN:Ali\nTEL:05321234567\nEND:VCARD\n"));

        Assert.Throws<InvalidDataException>(() => book.Import(File("bos.csv", "Name\n")));
        Assert.Single(book.All);
    }

    [Fact]
    public void Search_folds_turkish_letters_and_matches_numbers_in_any_format()
    {
        var book = Book();
        book.Save([
            new Contact("Özlem Yılmaz", ["+90 532 123 45 67"], []),
            new Contact("Ahmet Kaya", ["0505 111 22 33"], ["ahmet@example.com"]),
        ]);

        Assert.Equal("Özlem Yılmaz", book.Best("ozlem")!.Name);
        Assert.Equal("Ahmet Kaya", book.Best("ahmet")!.Name);
        Assert.Equal("Özlem Yılmaz", book.Search("0532 123").First().Contact.Name);
        Assert.Equal("Ahmet Kaya", book.Search("5051112233").First().Contact.Name);
        Assert.Empty(book.Search("zzz"));
        Assert.Equal(2, book.Search("").Count);
    }

    [Fact]
    public void Search_finds_text_anywhere_in_name_email_or_company_and_ranks_prefix_first()
    {
        var book = Book();
        book.Save([
            new Contact("Mehmet Ali", ["1"], []),
            new Contact("Ali Kaya", ["2"], []),
            new Contact("Zeynep Demir", ["3"], ["zeynep@alimarket.com"]),
            new Contact("Kalina", ["4"], []),
            new Contact("Ayşe", ["5"], [], "Lumora Yazılım"),
            new Contact("Ahmet", ["6"], []),
        ]);

        Assert.Equal(["Ali Kaya", "Mehmet Ali", "Kalina", "Zeynep Demir"], book.Search("ali").Select(m => m.Contact.Name));
        Assert.Equal(["Ayşe"], book.Search("yazilim").Select(m => m.Contact.Name));
        Assert.Equal(["Mehmet Ali"], book.Search("ali meh").Select(m => m.Contact.Name));

        // Harfleri sırayla içeren ama yazılanı içermeyen ("a…h…m" Ahmet değil) eşleşmez.
        Assert.DoesNotContain(book.Search("ahm"), m => m.Contact.Name == "Ayşe");
        Assert.Equal(["Ahmet"], book.Search("ahm").Select(m => m.Contact.Name));
    }

    [Fact]
    public void Empty_search_lists_everyone_alphabetically()
    {
        var book = Book();
        book.Save(Enumerable.Range(1, 45).Select(i => new Contact($"Kişi {i:00}", [$"{i}"], [])).Append(new Contact("Çağla", ["x"], [])).ToList());

        var all = book.Search("");
        Assert.Equal(46, all.Count);
        Assert.Equal("Çağla", all[0].Contact.Name);
    }

    [Fact]
    public void Clear_removes_file()
    {
        var book = Book();
        book.Save([new Contact("Ali", ["1"], [])]);
        book.Clear();

        Assert.Empty(book.All);
        Assert.Empty(Book().All);
    }

    [Fact]
    public void Corrupt_file_opens_empty()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllBytes(Path.Combine(_dir, "contacts.dat"), [1, 2, 3]);

        Assert.Empty(Book().All);
    }
}

public sealed class ContactsPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumorago-cplugin-" + Guid.NewGuid().ToString("N"));
    private readonly NullHost _host = new();
    private readonly List<string> _opened = [];
    private bool _global = true;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private ContactBook Book(params Contact[] contacts)
    {
        var book = new ContactBook(Path.Combine(_dir, "c.dat"), b => b, b => b);
        if (contacts.Length > 0)
        {
            book.Save(contacts);
        }

        return book;
    }

    private ContactsPlugin Plugin(ContactBook book, string? keyword = null)
    {
        var plugin = new ContactsPlugin(book, _opened.Add, () => _global, () => _opened.Add("ayarlar"), keyword);
        plugin.Init(new PluginContext(plugin.Metadata, _dir, _host));
        return plugin;
    }

    private static Task<IReadOnlyList<Result>> Ask(ContactsPlugin plugin, string text) =>
        plugin.QueryAsync(new Query(text, plugin.Metadata.ActionKeyword, text), CancellationToken.None);

    private static readonly Contact Ozlem = new("Özlem Yılmaz", ["0532 123 45 67", "0212 555 00 11"], ["ozlem@example.com"], "Lumora");

    [Fact]
    public async Task Enter_copies_ctrl_opens_whatsapp_shift_opens_mail()
    {
        var row = Assert.Single(await Ask(Plugin(Book(Ozlem), ContactsPlugin.Keyword), "özlem"));

        Assert.Equal("0532 123 45 67 · ozlem@example.com · Lumora", row.Subtitle);
        Assert.Contains("E-posta: ozlem@example.com", row.PreviewText);

        row.Action(new ActionContext(ActionModifiers.None));
        row.Action(new ActionContext(ActionModifiers.Ctrl));
        row.Action(new ActionContext(ActionModifiers.Shift));

        Assert.Equal(["0532 123 45 67"], _host.Copied);
        Assert.Equal(["https://wa.me/905321234567", "mailto:ozlem@example.com"], _opened);
    }

    [Fact]
    public async Task Menu_has_call_whatsapp_and_copy_for_every_number()
    {
        var row = (await Ask(Plugin(Book(Ozlem), ContactsPlugin.Keyword), "ozlem")).Single();
        var titles = row.ContextMenu!.Select(a => a.Title).ToList();

        Assert.Contains("Ara: 0212 555 00 11", titles);
        Assert.Contains("WhatsApp: 0532 123 45 67", titles);
        Assert.Contains("Mail at: ozlem@example.com", titles);

        row.ContextMenu!.First(a => a.Title == "Ara: 0212 555 00 11").Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["tel:+902125550011"], _opened);
    }

    [Fact]
    public async Task Global_search_follows_the_setting()
    {
        var plugin = Plugin(Book(Ozlem));

        Assert.Single(await Ask(plugin, "özlem"));
        Assert.Empty(await Ask(plugin, "ö"));

        _global = false;
        Assert.Empty(await Ask(plugin, "özlem"));
    }

    [Fact]
    public async Task Keyword_lists_more_than_twenty_and_filters_by_contained_text()
    {
        var people = Enumerable.Range(1, 30).Select(i => new Contact($"Kişi {i:00}", [$"0532000{i:0000}"], [])).ToList();
        people.Add(new Contact("Mehmet Ali", ["0505"], []));
        var plugin = Plugin(Book([.. people]), "/rehber");

        Assert.Equal(31, (await Ask(plugin, "")).Count);
        Assert.Equal(["Mehmet Ali"], (await Ask(plugin, "ali")).Select(r => r.Title));
        Assert.Equal("lumorago.contacts.kw.rehber", plugin.Metadata.Id);
    }

    [Fact]
    public async Task Global_search_shows_only_word_start_matches()
    {
        var plugin = Plugin(Book(new Contact("Mehmet Ali", ["1"], []), new Contact("Kalina", ["2"], [])));

        Assert.Equal(["Mehmet Ali"], (await Ask(plugin, "ali")).Select(r => r.Title));
    }

    [Fact]
    public async Task Empty_book_points_to_settings()
    {
        var row = Assert.Single(await Ask(Plugin(Book(), ContactsPlugin.Keyword), ""));

        Assert.Equal("Rehber boş", row.Title);
        row.Action(new ActionContext(ActionModifiers.None));
        Assert.Equal(["ayarlar"], _opened);
    }
}
