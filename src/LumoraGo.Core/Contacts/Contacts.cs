using System.Text;
using System.Text.Json;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Contacts;

/// <summary>Rehberdeki bir kişi. Telefonlar dosyada yazıldığı gibi tutulur, aranırken <see cref="PhoneNumber"/> ile çevrilir.</summary>
public sealed record Contact(string Name, IReadOnlyList<string> Phones, IReadOnlyList<string> Emails, string? Organization = null)
{
    public string Summary()
    {
        var parts = new List<string>();
        parts.AddRange(Phones.Take(1));
        parts.AddRange(Emails.Take(1));
        if (Organization is { Length: > 0 } org)
        {
            parts.Add(org);
        }

        return string.Join(" · ", parts);
    }

    public string FullText()
    {
        var lines = new List<string> { Name };
        if (Organization is { Length: > 0 } org)
        {
            lines.Add(org);
        }

        lines.Add("");
        lines.AddRange(Phones.Select(p => "Telefon: " + p));
        lines.AddRange(Emails.Select(e => "E-posta: " + e));
        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}

/// <summary>Türkiye numaralarını tek biçime çevirir: 0532…, +90 532…, 532… hepsi +90532… olur.</summary>
public static class PhoneNumber
{
    public static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Uluslararası biçim (+905321234567). Çevrilemiyorsa yalnızca rakamlar, başında + varsa korunur.</summary>
    public static string Normalize(string raw)
    {
        var trimmed = raw.Trim();
        var digits = Digits(trimmed);
        if (trimmed.StartsWith('+'))
        {
            return "+" + digits;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return "+" + digits[2..];
        }

        return digits.Length switch
        {
            11 when digits[0] == '0' => "+90" + digits[1..],
            10 when digits[0] is '5' or '2' or '3' or '4' => "+90" + digits,
            12 when digits.StartsWith("90", StringComparison.Ordinal) => "+" + digits,
            _ => digits,
        };
    }

    /// <summary>wa.me ve tel: için + işaretsiz rakamlar.</summary>
    public static string WhatsAppDigits(string raw) => Normalize(raw).TrimStart('+');
}

/// <summary>vCard (.vcf) ve CSV (Google, Outlook) kişi dosyalarını okur.</summary>
public static class ContactImport
{
    public static IReadOnlyList<Contact> Parse(string text, string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ParseCsv(text) : ParseVCard(text);

    public static IReadOnlyList<Contact> ParseVCard(string text)
    {
        var contacts = new List<Contact>();
        string? fullName = null;
        string? structured = null;
        string? org = null;
        var phones = new List<string>();
        var emails = new List<string>();
        var inside = false;

        foreach (var line in Unfold(text))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parameters = head.Split(';');
            var property = parameters[0];
            var dot = property.LastIndexOf('.');
            if (dot >= 0)
            {
                // Apple "item1.TEL" gibi grup adlarını at.
                property = property[(dot + 1)..];
            }

            property = property.ToUpperInvariant();
            if (property == "BEGIN" && value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                inside = true;
                fullName = structured = org = null;
                phones = [];
                emails = [];
                continue;
            }

            if (!inside)
            {
                continue;
            }

            value = Decode(value, parameters);
            switch (property)
            {
                case "FN":
                    fullName = Unescape(value);
                    break;
                case "N":
                    var n = SplitUnescaped(value);
                    structured = string.Join(" ", new[] { Get(n, 3), Get(n, 1), Get(n, 2), Get(n, 0), Get(n, 4) }.Where(s => s.Length > 0));
                    break;
                case "ORG":
                    org = string.Join(" ", SplitUnescaped(value).Where(s => s.Length > 0));
                    break;
                case "TEL":
                    AddDistinct(phones, value.Replace("tel:", "", StringComparison.OrdinalIgnoreCase).Trim());
                    break;
                case "EMAIL":
                    AddDistinct(emails, Unescape(value).Trim());
                    break;
                case "END":
                    inside = false;
                    Add(contacts, fullName is { Length: > 0 } ? fullName : structured, phones, emails, org);
                    break;
            }
        }

        return contacts;
    }

    public static IReadOnlyList<Contact> ParseCsv(string text)
    {
        var rows = CsvRows(text).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0].Select(h => FuzzyMatcher.Fold(h.Trim().Trim('﻿'))).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        IEnumerable<int> Cols(Func<string, bool> match) => header.Select((h, i) => (h, i)).Where(x => match(x.h)).Select(x => x.i);

        var nameCol = Col("name", "display name", "ad", "isim");
        var firstCol = Col("first name", "given name", "ad");
        var middleCol = Col("middle name", "additional name");
        var lastCol = Col("last name", "family name", "soyad", "soyadi");
        var orgCol = Col("organization name", "organization 1 - name", "company", "sirket");
        var phoneCols = Cols(h => (h.Contains("phone") || h.Contains("telefon")) && (h.Contains("value") || !h.Contains("type") && !h.Contains("label"))).ToList();
        var mailCols = Cols(h => (h.Contains("e-mail") || h.Contains("email") || h.Contains("e-posta")) && (h.Contains("value") || h.Contains("address") || !h.Contains("type") && !h.Contains("label") && !h.Contains("display"))).ToList();

        var contacts = new List<Contact>();
        foreach (var row in rows.Skip(1))
        {
            string Cell(int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
            var name = Cell(nameCol);
            if (name.Length == 0)
            {
                name = string.Join(" ", new[] { Cell(firstCol), Cell(middleCol), Cell(lastCol) }.Where(s => s.Length > 0));
            }

            var phones = new List<string>();
            foreach (var value in phoneCols.SelectMany(i => Split(Cell(i))))
            {
                AddDistinct(phones, value);
            }

            var emails = new List<string>();
            foreach (var value in mailCols.SelectMany(i => Split(Cell(i))))
            {
                AddDistinct(emails, value);
            }

            Add(contacts, name, phones, emails, Cell(orgCol));
        }

        return contacts;

        // Google birden fazla değeri " ::: " ile ayırır.
        static IEnumerable<string> Split(string cell) =>
            cell.Split(":::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Add(List<Contact> contacts, string? name, List<string> phones, List<string> emails, string? org)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = emails.FirstOrDefault() ?? phones.FirstOrDefault();
        }

        if (string.IsNullOrEmpty(name) || phones.Count + emails.Count == 0)
        {
            return;
        }

        contacts.Add(new Contact(name, phones, emails, string.IsNullOrWhiteSpace(org) ? null : org.Trim()));
    }

    private static void AddDistinct(List<string> list, string value)
    {
        if (value.Length > 0 && !list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }

    private static string Get(List<string> parts, int index) => index < parts.Count ? parts[index].Trim() : "";

    /// <summary>vCard'da boşluk ya da sekmeyle başlayan satır bir öncekinin devamıdır; quoted-printable "=" ile biten satır da.</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                current.Append(raw.AsSpan(1));
                continue;
            }

            if (current.Length > 0 && current[^1] == '=' && current.ToString().Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                current.Length--;
                current.Append(raw);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }

            current.Clear().Append(raw);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>Eski telefonların yazdığı ENCODING=QUOTED-PRINTABLE;CHARSET=UTF-8 değerlerini çözer.</summary>
    private static string Decode(string value, string[] parameters)
    {
        if (!parameters.Any(p => p.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string Unescape(string value) =>
        value.Replace("\\n", " ", StringComparison.OrdinalIgnoreCase).Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    private static List<string> SplitUnescaped(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                current.Append(value[++i]);
            }
            else if (value[i] == ';')
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(value[i]);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static IEnumerable<List<string>> CsvRows(string text)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c is ',' or ';' && (c == ',' || !text.Contains(',')))
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(v => v.Length > 0))
                {
                    yield return row;
                }

                row = [];
            }
            else
            {
                cell.Append(c);
            }
        }

        row.Add(cell.ToString());
        if (row.Any(v => v.Length > 0))
        {
            yield return row;
        }
    }
}

/// <summary>Kişilerin korumalı (Windows'ta DPAPI) dosyası ve bulanık arama.</summary>
public sealed class ContactBook
{
    private readonly string _path;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;
    private readonly ILog _log;
    private IReadOnlyList<(Contact Contact, string Name, string Haystack, string[] Phones)> _index = [];

    public ContactBook(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, ILog? log = null)
    {
        _path = path;
        _protect = protect;
        _unprotect = unprotect;
        _log = log ?? NullLog.Instance;
        Set(Load());
    }

    public IReadOnlyList<Contact> All { get; private set; } = [];

    public event EventHandler? Changed;

    /// <summary>Dosyayı okuyup rehberi onunla değiştirir.</summary>
    /// <returns>İçe aktarılan kişi sayısı.</returns>
    public int Import(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var text = DecodeText(bytes);
        var contacts = ContactImport.Parse(text, file);
        if (contacts.Count == 0)
        {
            throw new InvalidDataException("Dosyada kişi bulunamadı. Google Kişiler'den vCard ya da Google CSV olarak dışa aktar.");
        }

        Save(contacts);
        return contacts.Count;
    }

    public void Save(IReadOnlyList<Contact> contacts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, _protect(JsonSerializer.SerializeToUtf8Bytes(contacts)));
        File.Move(temp, _path, overwrite: true);
        Set(contacts);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        Set([]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Yazılanın geçtiği kişiler: her kelime adda, e-postada ya da şirkette geçmeli (Türkçe harfsiz de olur);
    /// en az 4 rakam yazılırsa numaranın içinde de aranır. Ad yazılanla başlayanlar önce, sonra kelime başı, sonra içinde geçenler.
    /// </summary>
    public IReadOnlyList<(Contact Contact, int Score)> Search(string text)
    {
        var query = text.Trim();
        if (query.Length == 0)
        {
            return All.OrderBy(c => c.Name, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true)).Select(c => (c, 100)).ToList();
        }

        var folded = FuzzyMatcher.Fold(query);
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var digits = PhoneNumber.Digits(query);
        var byNumber = digits.Length >= 4 && digits.Length * 2 >= query.Count(c => !char.IsWhiteSpace(c));
        var matches = new List<(Contact, int)>();
        foreach (var (contact, name, haystack, phones) in _index)
        {
            var score = 0;
            if (words.All(w => haystack.Contains(w, StringComparison.Ordinal)))
            {
                score = name.StartsWith(folded, StringComparison.Ordinal) ? 900
                    : words.All(w => name.Split(' ').Any(n => n.StartsWith(w, StringComparison.Ordinal))) ? 700
                    : words.All(w => name.Contains(w, StringComparison.Ordinal)) ? 500
                    : 300;
            }

            if (byNumber && phones.Any(p => p.Contains(digits.TrimStart('0'), StringComparison.Ordinal)))
            {
                score = Math.Max(score, 600);
            }

            if (score > 0)
            {
                matches.Add((contact, score));
            }
        }

        var tr = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
        return matches.OrderByDescending(m => m.Item2).ThenBy(m => m.Item1.Name, tr).ToList();
    }

    /// <summary>"ahmet" için en iyi eşleşen kişi; hiç eşleşme yoksa null.</summary>
    public Contact? Best(string name) => Search(name).Select(m => m.Contact).FirstOrDefault();

    private void Set(IReadOnlyList<Contact> contacts)
    {
        All = contacts;
        _index = contacts.Select(c =>
        {
            var name = FuzzyMatcher.Fold(c.Name);
            var haystack = string.Join(' ', new[] { name, FuzzyMatcher.Fold(c.Organization ?? "") }.Concat(c.Emails.Select(FuzzyMatcher.Fold)));
            return (c, name, haystack, c.Phones.Select(p => PhoneNumber.Normalize(p)).ToArray());
        }).ToList();
    }

    private IReadOnlyList<Contact> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<Contact>>(_unprotect(File.ReadAllBytes(_path))) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _log.Warn($"Kişiler okunamadı, boş açılıyor: {ex.Message}");
            return [];
        }
    }

    /// <summary>UTF-8 (BOM'lu ya da değil) ve UTF-16; Outlook'un Windows-1254 CSV'si için geçerli UTF-8 değilse Türkçe kod sayfası.</summary>
    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254).GetString(bytes);
        }
    }
}
