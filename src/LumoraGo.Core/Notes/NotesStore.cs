using System.Text.Json;

namespace LumoraGo.Core.Notes;

public sealed record Note(Guid Id, string Text, bool Done, DateTime CreatedAt);

/// <summary>Hızlı notlar ve yapılacaklar. Her değişiklikte JSON dosyasına yazılır.</summary>
public sealed class NotesStore
{
    private readonly string? _path;
    private readonly ILog _log;
    private readonly object _gate = new();
    private List<Note> _notes = [];

    /// <param name="path">Notların dosyası; null ise sadece bellekte tutulur (testler için).</param>
    public NotesStore(string? path, ILog? log = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        if (path is not null && File.Exists(path))
        {
            try
            {
                _notes = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(path)) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _log.Warn($"Notlar okunamadı: {ex.Message}");
            }
        }
    }

    /// <summary>Önce yapılacaklar (en yeni üstte), sonra tamamlananlar.</summary>
    public IReadOnlyList<Note> Notes
    {
        get
        {
            lock (_gate)
            {
                return _notes.OrderBy(n => n.Done).ThenByDescending(n => n.CreatedAt).ToList();
            }
        }
    }

    public Note Add(string text, DateTime? now = null)
    {
        var note = new Note(Guid.NewGuid(), text.Trim(), false, now ?? DateTime.Now);
        Change(notes => notes.Add(note));
        return note;
    }

    public void Toggle(Guid id) => Change(notes =>
    {
        var index = notes.FindIndex(n => n.Id == id);
        if (index >= 0)
        {
            notes[index] = notes[index] with { Done = !notes[index].Done };
        }
    });

    public void Remove(Guid id) => Change(notes => notes.RemoveAll(n => n.Id == id));

    public void RemoveDone() => Change(notes => notes.RemoveAll(n => n.Done));

    private void Change(Action<List<Note>> change)
    {
        lock (_gate)
        {
            change(_notes);
            if (_path is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_notes));
                File.Move(temp, _path, overwrite: true);
            }
            catch (IOException ex)
            {
                _log.Warn($"Notlar kaydedilemedi: {ex.Message}");
            }
        }
    }
}
