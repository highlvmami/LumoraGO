using System.Text;
using System.Text.Json;

namespace LumoraGo.Core.Info;

/// <param name="Kind">Söz türü: isim, sıfat, noun…</param>
public sealed record Sense(string Meaning, string? Kind, string? Example);

/// <param name="Phrases">Atasözü ve deyimler (TDK) ya da yok.</param>
public sealed record DictionaryEntry(string Word, string Source, string Url, IReadOnlyList<Sense> Senses, IReadOnlyList<string> Synonyms, IReadOnlyList<string> Phrases, string? Phonetic)
{
    /// <summary>Önizleme panelinde gösterilecek tam metin.</summary>
    public string FullText()
    {
        var text = new StringBuilder();
        text.Append(Word);
        if (Phonetic is { Length: > 0 })
        {
            text.Append("  ").Append(Phonetic);
        }

        text.Append("\n\n");
        for (var i = 0; i < Senses.Count; i++)
        {
            var s = Senses[i];
            text.Append(i + 1).Append(". ");
            if (s.Kind is { Length: > 0 })
            {
                text.Append('(').Append(s.Kind).Append(") ");
            }

            text.Append(s.Meaning).Append('\n');
            if (s.Example is { Length: > 0 })
            {
                text.Append("   “").Append(s.Example).Append("”\n");
            }
        }

        if (Synonyms.Count > 0)
        {
            text.Append("\nEş anlamlılar: ").Append(string.Join(", ", Synonyms)).Append('\n');
        }

        if (Phrases.Count > 0)
        {
            text.Append("\nAtasözü ve deyimler:\n").Append(string.Join("\n", Phrases.Select(p => "• " + p))).Append('\n');
        }

        return text.Append("\nKaynak: ").Append(Source).ToString();
    }
}

/// <summary>Türkçe için TDK Güncel Türkçe Sözlük, İngilizce için dictionaryapi.dev.</summary>
public sealed class DictionaryLookup(HttpClient http)
{
    public const string TdkUrl = "https://sozluk.gov.tr/gts?ara=";
    public const string EnglishUrl = "https://api.dictionaryapi.dev/api/v2/entries/en/";

    public async Task<DictionaryEntry?> TurkishAsync(string word, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http, TdkUrl + Uri.EscapeDataString(word), ct);
        return json is null ? null : ParseTdk(word, json.RootElement);
    }

    public async Task<DictionaryEntry?> EnglishAsync(string word, CancellationToken ct)
    {
        using var json = await InfoHttp.GetJsonAsync(http, EnglishUrl + Uri.EscapeDataString(word), ct);
        return json is null ? null : ParseEnglish(word, json.RootElement);
    }

    /// <summary>TDK bulunamayınca {"error": "..."} döndürür; kelime eş sesliyse birden çok madde gelir, hepsi birleştirilir.</summary>
    internal static DictionaryEntry? ParseTdk(string word, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var senses = new List<Sense>();
        var phrases = new List<string>();
        string? title = null;
        foreach (var entry in root.EnumerateArray())
        {
            title ??= entry.Str("madde");
            foreach (var meaning in entry.Arr("anlamlarListe"))
            {
                if (meaning.Str("anlam") is not { Length: > 0 } text)
                {
                    continue;
                }

                var kind = meaning.Arr("ozelliklerListe").Select(o => o.Str("tam_adi")).FirstOrDefault(k => k is { Length: > 0 });
                var example = meaning.Arr("orneklerListe").Select(o => o.Str("ornek")).FirstOrDefault(x => x is { Length: > 0 });
                senses.Add(new Sense(text.Trim(), kind, example?.Trim()));
            }

            phrases.AddRange(entry.Arr("atasozu").Select(a => a.Str("madde")).OfType<string>().Where(p => p.Length > 0));
        }

        if (senses.Count == 0)
        {
            return null;
        }

        var shown = title ?? word;
        return new DictionaryEntry(shown, "TDK Güncel Türkçe Sözlük", "https://www.google.com/search?q=" + Uri.EscapeDataString(shown + " tdk anlamı"),
            senses, [], phrases.Distinct().ToList(), null);
    }

    internal static DictionaryEntry? ParseEnglish(string word, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var senses = new List<Sense>();
        var synonyms = new List<string>();
        string? title = null;
        string? phonetic = null;
        foreach (var entry in root.EnumerateArray())
        {
            title ??= entry.Str("word");
            phonetic ??= entry.Str("phonetic") ?? entry.Arr("phonetics").Select(p => p.Str("text")).FirstOrDefault(t => t is { Length: > 0 });
            foreach (var meaning in entry.Arr("meanings"))
            {
                var kind = meaning.Str("partOfSpeech");
                foreach (var definition in meaning.Arr("definitions"))
                {
                    if (definition.Str("definition") is { Length: > 0 } text)
                    {
                        senses.Add(new Sense(text.Trim(), kind, definition.Str("example")?.Trim()));
                    }

                    synonyms.AddRange(definition.Arr("synonyms").Select(s => s.GetString()).OfType<string>());
                }

                synonyms.AddRange(meaning.Arr("synonyms").Select(s => s.GetString()).OfType<string>());
            }
        }

        if (senses.Count == 0)
        {
            return null;
        }

        var shown = title ?? word;
        return new DictionaryEntry(shown, "dictionaryapi.dev (Wiktionary)", "https://www.merriam-webster.com/dictionary/" + Uri.EscapeDataString(shown),
            senses, synonyms.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(), [], phonetic);
    }
}
