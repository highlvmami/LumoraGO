using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Kullanıcının ayarlardan eklediği kısayol: bir kelime yazınca bir siteyi, dosyayı, klasörü veya uygulamayı açar.</summary>
/// <param name="Keyword">Tetik kelimesi, örn. "/okul" veya "mail".</param>
/// <param name="Name">Sonuçta görünen ad; boşsa hedef gösterilir.</param>
/// <param name="Target">Adres veya yol. İçinde "{0}" varsa kelimeden sonra yazılan metin oraya (URL kodlanarak) konur.</param>
public sealed record CustomShortcut(string Keyword, string Name, string Target)
{
    public bool IsTemplate => Target.Contains("{0}", StringComparison.Ordinal);

    public string Resolve(string argument) =>
        IsTemplate ? Target.Replace("{0}", Uri.EscapeDataString(argument), StringComparison.Ordinal) : Target;
}

/// <summary>
/// Özel kısayolları çalıştırır. Kayıtlı bir tetik kelimesi olmadığı için genel aramaya katılır; böylece ayarlardan
/// eklenen kısayol yeniden başlatmadan çalışır. Yerleşik tetik kelimeleri her zaman önce gelir.
/// </summary>
public sealed class CustomShortcutsPlugin(Func<IReadOnlyList<CustomShortcut>> shortcuts, Action<string> open) : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.custom", "Özel kısayollar");

    /// <summary>Tam kelime eşleşmesi uygulama sonuçlarının üstünde dursun.</summary>
    public const int ExactScore = 5000;

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search;
        var space = text.IndexOf(' ');
        var word = space < 0 ? text : text[..space];
        var argument = space < 0 ? "" : text[(space + 1)..].Trim();

        var results = new List<Result>();
        foreach (var shortcut in shortcuts())
        {
            var keyword = shortcut.Keyword.Trim();
            if (keyword.Length == 0 || string.IsNullOrWhiteSpace(shortcut.Target))
            {
                continue;
            }

            var exact = string.Equals(word, keyword, StringComparison.OrdinalIgnoreCase);
            // Sadece kelime yazılıyorsa ("/ok" → "/okul") öneri olarak düşük puanla göster.
            var prefix = !exact && space < 0 && word.Length >= 2 && keyword.StartsWith(word, StringComparison.OrdinalIgnoreCase);
            if (!exact && !prefix)
            {
                continue;
            }

            var target = shortcut.Resolve(argument);
            var name = shortcut.Name.Trim().Length > 0 ? shortcut.Name.Trim() : shortcut.Target;
            var title = shortcut.IsTemplate && argument.Length > 0 ? $"{name}: {argument}" : name;
            results.Add(new Result(title, $"{keyword} · {target}", null, exact ? ExactScore : 50, _ =>
            {
                open(target);
                return true;
            })
            {
                AutoComplete = keyword + " ",
            });
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
