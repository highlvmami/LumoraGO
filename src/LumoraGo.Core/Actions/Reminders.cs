using System.Text.Json;

namespace LumoraGo.Core.Actions;

/// <param name="Due">Yerel saat.</param>
/// <param name="Notified">Bildirimi gösterildi; listede hâlâ durur, Enter ile tamamlanır.</param>
public sealed record Reminder(Guid Id, string Text, DateTime Due, bool Notified = false);

/// <summary>
/// Hatırlatıcılar. reminders.json'da tutulur; <see cref="TakeDue"/> düzenli çağrılır ve vakti gelenleri bir kez verir.
/// LumoraGo kapalıyken vakti geçenler açılışta ilk çağrıda gelir.
/// </summary>
public sealed class ReminderStore
{
    private readonly string? _path;
    private readonly ILog _log;
    private readonly object _gate = new();
    private List<Reminder> _items = [];

    /// <param name="path">Dosya; null ise yalnızca bellekte (testler için).</param>
    public ReminderStore(string? path, ILog? log = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        if (path is not null && File.Exists(path))
        {
            try
            {
                _items = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(path)) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _log.Warn($"Hatırlatıcılar okunamadı: {ex.Message}");
            }
        }
    }

    public event EventHandler? Changed;

    /// <summary>Vakti en yakın olan önce.</summary>
    public IReadOnlyList<Reminder> All
    {
        get
        {
            lock (_gate)
            {
                return _items.OrderBy(r => r.Due).ToList();
            }
        }
    }

    public Reminder Add(string text, DateTime due)
    {
        var reminder = new Reminder(Guid.NewGuid(), text.Trim(), due);
        Change(items => items.Add(reminder));
        return reminder;
    }

    public void Remove(Guid id) => Change(items => items.RemoveAll(r => r.Id == id));

    public void Snooze(Guid id, DateTime until) => Change(items =>
    {
        var index = items.FindIndex(r => r.Id == id);
        if (index >= 0)
        {
            items[index] = items[index] with { Due = until, Notified = false };
        }
    });

    /// <summary>Vakti gelmiş ve henüz bildirilmemiş olanları döndürür ve bildirildi işaretler.</summary>
    public IReadOnlyList<Reminder> TakeDue(DateTime now)
    {
        var due = new List<Reminder>();
        Change(items =>
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].Notified && items[i].Due <= now)
                {
                    due.Add(items[i]);
                    items[i] = items[i] with { Notified = true };
                }
            }
        }, notify: false);
        return due.OrderBy(r => r.Due).ToList();
    }

    private void Change(Action<List<Reminder>> change, bool notify = true)
    {
        lock (_gate)
        {
            var before = _items.ToList();
            change(_items);
            if (before.SequenceEqual(_items))
            {
                return;
            }

            if (_path is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(_items));
                    File.Move(temp, _path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"Hatırlatıcılar kaydedilemedi: {ex.Message}");
                }
            }
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
