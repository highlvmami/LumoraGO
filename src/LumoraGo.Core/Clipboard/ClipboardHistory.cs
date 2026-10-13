namespace LumoraGo.Core.Clipboard;

public sealed record ClipboardEntry(string Text, DateTime CopiedAt);

/// <summary>
/// Kopyalanan metinlerin listesi, en yenisi başta. Sadece bellekte tutulur; LumoraGo kapanınca silinir.
/// Aynı metin tekrar kopyalanınca başa taşınır.
/// </summary>
public sealed class ClipboardHistory(int capacity = 200)
{
    /// <summary>Bundan uzun metinler kaydedilmez (büyük günlük dosyaları gibi).</summary>
    public const int MaxTextLength = 100_000;

    private readonly object _gate = new();
    private readonly LinkedList<ClipboardEntry> _entries = new();

    public IReadOnlyList<ClipboardEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public void Add(string? text, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            return;
        }

        lock (_gate)
        {
            RemoveText(text);
            _entries.AddFirst(new ClipboardEntry(text, now ?? DateTime.Now));
            while (_entries.Count > capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    public void Remove(string text)
    {
        lock (_gate)
        {
            RemoveText(text);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void RemoveText(string text)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (node.Value.Text == text)
            {
                _entries.Remove(node);
                return;
            }
        }
    }
}
