using LumoraGo.Core.Ai;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// Yapay zekâ: "? soru" ya da "/ai soru" Claude'a sorar; "/ai özetle", "çevir", "düzelt" panodaki metin üzerinde çalışır.
/// İstek yalnızca Enter'a basınca gider (yazarken her harfte para harcanmasın); yanıt aynı listede görünür.
/// </summary>
public sealed class AiPlugin : IPlugin
{
    public const string Keyword = "/ai";
    public const string Prefix = "?";

    private const string General =
        "Sen LumoraGo adlı masaüstü başlatıcısındaki yardımcısın. Kullanıcı soruyu hızlıca yazıp yanıtı launcher penceresinde okuyor. " +
        "Kullanıcı hangi dilde yazdıysa o dilde (çoğunlukla Türkçe) kısa ve doğrudan yanıt ver; giriş cümlesi, tekrar ve kapanış yazma. " +
        "Düz metin kullan: başlık, tablo ve kalın yazı yok; gerekirse kısa maddeler.";

    public static readonly IReadOnlyList<(string Id, string Title, string Words, string Instructions)> Actions =
    [
        ("ozet", "Panodakini özetle", "ozetle ozet kisalt summarize",
            "Kullanıcının verdiği metni Türkçe olarak birkaç kısa maddeyle özetle. Yalnızca özeti yaz."),
        ("tr", "Panodakini Türkçeye çevir", "cevir turkce translate turkish",
            "Kullanıcının verdiği metni doğal bir Türkçeye çevir. Yalnızca çeviriyi yaz."),
        ("en", "Panodakini İngilizceye çevir", "cevir ingilizce english translate",
            "Translate the user's text into natural English. Write only the translation."),
        ("duzelt", "Panodakinin yazımını düzelt", "duzelt yazim imla dilbilgisi fix grammar",
            "Kullanıcının verdiği metnin yazım, noktalama ve dil bilgisi hatalarını düzelt. Anlamı, dili ve üslubu koru. Yalnızca düzeltilmiş metni yaz."),
        ("resmi", "Panodakini resmîleştir", "resmi kibar profesyonel formal",
            "Kullanıcının verdiği metni aynı dilde, kibar ve profesyonel bir üslupla yeniden yaz. Yalnızca yeni metni yaz."),
    ];

    private readonly IAssistant _assistant;
    private readonly Func<string?> _clipboard;
    private readonly Action<string> _paste;
    private readonly Action _openSettings;
    private readonly string? _keyword;
    private readonly object _gate = new();
    private readonly Dictionary<string, (Task<string>? Running, string? Answer, string? Error)> _answers = [];
    private IPluginHost? _host;

    /// <param name="keyword">null ise "?" ile başlayan genel aramalara yanıt verir.</param>
    public AiPlugin(IAssistant assistant, Func<string?> clipboard, Action<string> paste, Action openSettings, string? keyword = Keyword)
    {
        _assistant = assistant;
        _clipboard = clipboard;
        _paste = paste;
        _openSettings = openSettings;
        _keyword = keyword;
        Metadata = keyword is null ? new("lumorago.ai", "Yapay zekâ") : new("lumorago.ai." + keyword.Trim('/'), "Yapay zekâ", keyword);
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        string text;
        if (_keyword is null)
        {
            var raw = query.Search.TrimStart();
            if (!raw.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<Result>>([]);
            }

            text = raw[Prefix.Length..].Trim();
        }
        else
        {
            text = query.Search.Trim();
        }

        var refresh = query.RawText.TrimEnd();
        var results = new List<Result>();
        if (!_assistant.IsConfigured)
        {
            results.Add(new Result("Yapay zekâ için API anahtarı gerekli", "Ayarlar → Yapay zekâ: console.anthropic.com'dan aldığın anahtarı yapıştır", null, 1000, _ =>
            {
                _openSettings();
                return true;
            }));
            return Task.FromResult<IReadOnlyList<Result>>(results);
        }

        var folded = FuzzyMatcher.Fold(text);
        var clip = _keyword is null ? null : _clipboard();
        if (_keyword is not null && clip is { Length: > 0 })
        {
            var shown = Actions.Where(a => folded.Length == 0 || FuzzyMatcher.Fold(a.Words).Split(' ').Any(w => w.StartsWith(folded, StringComparison.Ordinal)));
            foreach (var (action, i) in shown.Select((a, i) => (a, i)))
            {
                results.AddRange(Request(action.Title, "Pano: " + OneLine(clip, 70), action.Instructions, clip, 1500 - i, refresh));
            }
        }

        if (text.Length > 0)
        {
            results.AddRange(Request($"Claude'a sor: {text}", "Enter ile gönder", General, text, results.Count > 0 ? 1000 : 2000, refresh));
        }
        else if (results.Count == 0)
        {
            results.Add(new Result("Claude'a bir şey sor", _keyword is null ? "Örnek: ? İstanbul'un nüfusu kaç" : "Örnek: /ai mail için kibar bir ret cevabı yaz; panoda metin varsa /ai özetle, çevir, düzelt",
                null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private IEnumerable<Result> Request(string title, string subtitle, string instructions, string input, int score, string refresh)
    {
        var key = instructions + "\n" + input;
        (Task<string>? Running, string? Answer, string? Error) state;
        lock (_gate)
        {
            _answers.TryGetValue(key, out state);
        }

        if (state.Answer is { } answer)
        {
            return [AnswerResult(title, answer, score)];
        }

        if (state.Running is not null)
        {
            return [new Result("Claude düşünüyor…", title, null, score, _ => false)];
        }

        var row = new Result(title, state.Error is { } error ? $"⚠ {error} · Enter ile tekrar dene" : subtitle, null, score, _ =>
        {
            Start(key, instructions, input, refresh);
            return false;
        });
        return [row];
    }

    private void Start(string key, string instructions, string input, string refresh)
    {
        lock (_gate)
        {
            if (_answers.TryGetValue(key, out var existing) && existing.Running is not null)
            {
                return;
            }

            var task = _assistant.AskAsync(instructions, input, CancellationToken.None);
            _answers[key] = (task, null, null);
            _ = task.ContinueWith(t =>
            {
                lock (_gate)
                {
                    _answers[key] = t.IsCompletedSuccessfully
                        ? (null, t.Result, null)
                        : (null, null, t.Exception?.InnerException is AiException ai ? ai.Message : t.Exception?.InnerException?.Message ?? "iptal edildi");
                    if (_answers.Count > 30)
                    {
                        foreach (var old in _answers.Keys.Where(k => k != key).Take(_answers.Count - 30).ToList())
                        {
                            _answers.Remove(old);
                        }
                    }
                }

                _host?.RefreshResults(refresh);
            }, TaskScheduler.Default);
        }

        _host?.RefreshResults(refresh);
    }

    private Result AnswerResult(string request, string answer, int score)
    {
        var lines = answer.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        return new Result(OneLine(lines.FirstOrDefault() ?? answer, 110), "Enter: kopyala · Ctrl+Enter: yapıştır · Shift+Boşluk: tamamı", InfoPlugins.EmojiPrefix + "✨", score, ctx =>
        {
            if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
            {
                _paste(answer);
            }
            else
            {
                _host?.CopyToClipboard(answer);
            }

            return true;
        })
        {
            Card = new ResultCard(OneLine(request, 60), null, lines.Take(12).ToList()),
            PreviewText = answer,
            DragData = DragPayload.Text(answer),
        };
    }

    private static string OneLine(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > max ? line[..max] + "…" : line;
    }
}
