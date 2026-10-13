using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.DeepSearch;

public enum DeepSearchMatchKind
{
    /// <summary>Aranan kelime dosya veya klasör adında geçiyor.</summary>
    Name,

    /// <summary>Aranan kelime dosyanın içinde geçiyor.</summary>
    Content,
}

/// <param name="Line">İçerik eşleşmesinde ilk geçtiği satır (1'den başlar).</param>
/// <param name="Snippet">İçerik eşleşmesinde o satırdan kısa bir parça.</param>
/// <param name="ContentMatches">Kelimenin geçtiği satır sayısı.</param>
public sealed record DeepSearchHit(
    string Path,
    bool IsDirectory,
    DeepSearchMatchKind Kind,
    int Line = 0,
    string? Snippet = null,
    int ContentMatches = 0);

public sealed record DeepSearchSnapshot(
    string Term,
    IReadOnlyList<DeepSearchHit> Hits,
    long FilesScanned,
    TimeSpan Elapsed,
    bool IsRunning,
    bool WasStopped,
    bool HitLimitReached,
    string? CurrentFolder);

/// <summary>
/// Bir kelimeyi kök klasörlerin altındaki tüm dosya adlarında ve metin dosyalarının içinde arar. Arka planda çalışır,
/// bulduklarını hemen <see cref="Snapshot"/> ile verir. Hiçbir şeyi diske yazmaz.
/// </summary>
public sealed class DeepSearchJob
{
    private const int SnippetRadius = 40;
    private const int SniffBytes = 8000;

    // OneDrive gibi yer tutucu dosyaların içi okunursa buluttan indirilir; bunlara dokunma.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes CloudOnly = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    private readonly DeepSearchOptions _options;
    private readonly string _foldedTerm;
    private readonly ConcurrentQueue<DeepSearchHit> _hits = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _stopwatch = new();
    private long _filesScanned;
    private int _hitCount;
    private volatile bool _running = true;
    private volatile bool _stopped;
    private volatile bool _limitReached;
    private volatile string? _currentFolder;

    private DeepSearchJob(string term, DeepSearchOptions options)
    {
        Term = term;
        _options = options;
        _foldedTerm = FuzzyMatcher.Fold(term);
    }

    public string Term { get; }

    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Aramayı arka planda başlatır.</summary>
    /// <param name="startDelay">Kullanıcı yazmaya devam ederse boşa disk taramamak için başlamadan önce beklenen süre.</param>
    public static DeepSearchJob Start(string term, DeepSearchOptions options, TimeSpan startDelay = default)
    {
        var job = new DeepSearchJob(term, options);
        job.Completion = Task.Run(() => job.RunAsync(startDelay));
        return job;
    }

    public void Stop()
    {
        if (_running)
        {
            _stopped = true;
            _cts.Cancel();
        }
    }

    public DeepSearchSnapshot Snapshot() => new(
        Term,
        _hits.ToArray(),
        Interlocked.Read(ref _filesScanned),
        _stopwatch.Elapsed,
        _running,
        _stopped,
        _limitReached,
        _currentFolder);

    private async Task RunAsync(TimeSpan startDelay)
    {
        try
        {
            if (startDelay > TimeSpan.Zero)
            {
                await Task.Delay(startDelay, _cts.Token).ConfigureAwait(false);
            }

            _stopwatch.Start();
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = _options.Parallelism, CancellationToken = _cts.Token };
            foreach (var root in _options.Roots)
            {
                if (_limitReached)
                {
                    break;
                }

                if (!Directory.Exists(root))
                {
                    continue;
                }

                var entries = System.Collections.Concurrent.Partitioner.Create(Enumerate(root), EnumerablePartitionerOptions.NoBuffering);
                Parallel.ForEach(entries, parallel, Examine);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _stopwatch.Stop();
            _currentFolder = null;
            _running = false;
        }
    }

    private IEnumerable<Entry> Enumerate(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        return new FileSystemEnumerable<Entry>(
            root,
            (ref FileSystemEntry e) => new Entry(e.ToFullPath(), e.FileName.ToString(), e.IsDirectory, e.Attributes, e.Length),
            options)
        {
            ShouldRecursePredicate = (ref FileSystemEntry e) => !_options.SkipDirectoryNames.Contains(e.FileName.ToString()),
        };
    }

    private void Examine(Entry entry, ParallelLoopState state)
    {
        if (entry.IsDirectory)
        {
            _currentFolder = entry.Path;
            if (NameMatches(entry.Name))
            {
                Add(new DeepSearchHit(entry.Path, true, DeepSearchMatchKind.Name), state);
            }

            return;
        }

        Interlocked.Increment(ref _filesScanned);
        if (NameMatches(entry.Name))
        {
            Add(new DeepSearchHit(entry.Path, false, DeepSearchMatchKind.Name), state);
        }

        if (entry.Length == 0
            || entry.Length > _options.MaxContentBytes
            || (entry.Attributes & CloudOnly) != 0
            || _options.BinaryExtensions.Contains(System.IO.Path.GetExtension(entry.Name)))
        {
            return;
        }

        if (SearchContent(entry.Path) is { } hit)
        {
            Add(hit, state);
        }
    }

    private bool NameMatches(string name) => FuzzyMatcher.Fold(name).Contains(_foldedTerm, StringComparison.Ordinal);

    /// <summary>Dosya metinse içinde arar. İkili (NUL baytı olan) dosyalar ve okunamayanlar atlanır.</summary>
    internal DeepSearchHit? SearchContent(string path)
    {
        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (!LooksLikeText(bytes))
            {
                return null;
            }

            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var lineNumber = 0;
        var firstLine = 0;
        string? snippet = null;
        var matches = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            lineNumber++;
            var folded = FuzzyMatcher.Fold(line.ToString());
            var index = folded.IndexOf(_foldedTerm, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            matches++;
            if (snippet is null)
            {
                firstLine = lineNumber;
                snippet = MakeSnippet(line.ToString(), index, _foldedTerm.Length);
            }
        }

        return matches == 0 ? null : new DeepSearchHit(path, false, DeepSearchMatchKind.Content, firstLine, snippet, matches);
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        // UTF-16 metinlerde NUL baytı olur ama BOM ile başlar.
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return true;
        }

        return Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, SniffBytes)) < 0;
    }

    internal static string MakeSnippet(string line, int index, int length)
    {
        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(line.Length, index + length + SnippetRadius);
        var snippet = line[start..end].Trim();
        return (start > 0 ? "…" : "") + snippet + (end < line.Length ? "…" : "");
    }

    private void Add(DeepSearchHit hit, ParallelLoopState state)
    {
        if (Interlocked.Increment(ref _hitCount) > _options.MaxHits)
        {
            _limitReached = true;
            state.Stop();
            return;
        }

        _hits.Enqueue(hit);
    }

    private readonly record struct Entry(string Path, string Name, bool IsDirectory, FileAttributes Attributes, long Length);
}
