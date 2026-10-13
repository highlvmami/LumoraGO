using LumoraGo.Core.Notes;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"/not süt al" not ekler, "/not" listeler; Enter yapılacağı tamamlandı olarak işaretler.</summary>
public sealed class NotesPlugin(NotesStore store) : IPlugin
{
    public const string Keyword = "/not";
    public static readonly PluginMetadata Metadata = new("lumorago.notes", "Hızlı not ve yapılacaklar", Keyword);

    private const int AddScore = 10_000;
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var results = new List<Result>();
        var text = query.Search;
        if (text.Length > 0)
        {
            results.Add(new Result($"Not ekle: {text}", "Enter ile kaydet", null, AddScore, _ =>
            {
                store.Add(text);
                _host?.ChangeQuery(Keyword + " ");
                return false;
            }));
        }

        var folded = FuzzyMatcher.Fold(text);
        var notes = store.Notes
            .Where(n => folded.Length == 0 || FuzzyMatcher.Fold(n.Text).Contains(folded, StringComparison.Ordinal))
            .ToList();

        results.AddRange(notes.Select((n, i) => ToResult(n, notes.Count - i)));

        if (results.Count == 0)
        {
            results.Add(new Result("Henüz not yok", "\"/not süt al\" yazıp Enter'a bas", null, 1, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private Result ToResult(Note note, int score)
    {
        var subtitle = note.Done
            ? $"✓ Tamamlandı · {note.CreatedAt:dd.MM HH:mm} · Enter: geri al"
            : $"Yapılacak · {note.CreatedAt:dd.MM HH:mm} · Enter: tamamlandı";

        return new Result(note.Text, subtitle, null, score, _ => Toggle(note),
        [
            new ContextAction(note.Done ? "Geri al" : "Tamamlandı", _ => Toggle(note)),
            new ContextAction("Kopyala", _ =>
            {
                _host?.CopyToClipboard(note.Text);
                return true;
            }),
            new ContextAction("Sil", _ =>
            {
                store.Remove(note.Id);
                _host?.ChangeQuery(Keyword + " ");
                return false;
            }),
            new ContextAction("Tamamlananları temizle", _ =>
            {
                store.RemoveDone();
                _host?.ChangeQuery(Keyword + " ");
                return false;
            }),
        ]);
    }

    private bool Toggle(Note note)
    {
        store.Toggle(note.Id);
        _host?.RefreshResults(Keyword);
        return false;
    }
}
