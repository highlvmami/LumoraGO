using System.IO.Enumeration;
using LumoraGo.Core.Search;

namespace LumoraGo.Core.Files;

/// <summary>İndekslenmiş bir dosya veya klasör.</summary>
public sealed record FileEntry(string Path, string Name, bool IsDirectory)
{
    public string FoldedName { get; } = FuzzyMatcher.Fold(Name);
}

/// <summary>
/// Kullanıcı klasörlerindeki dosya ve klasör adlarının bellekteki listesi. Arka planda kurulur ve düzenli yenilenir;
/// arama sırasında diske dokunulmaz. Diske hiçbir şey yazılmaz.
/// </summary>
public sealed class FileIndex
{
    private readonly Func<IReadOnlyList<string>> _roots;
    private readonly IReadOnlySet<string> _skipDirectoryNames;
    private readonly ILog _log;
    private volatile IReadOnlyList<FileEntry> _entries = [];
    private int _building;

    /// <param name="roots">Her yenilemede çağrılır; ayarlarda değişen klasörler bir sonraki yenilemede geçerli olur.</param>
    public FileIndex(Func<IReadOnlyList<string>> roots, IReadOnlySet<string> skipDirectoryNames, ILog? log = null)
    {
        _roots = roots;
        _skipDirectoryNames = skipDirectoryNames;
        _log = log ?? NullLog.Instance;
    }

    public IReadOnlyList<FileEntry> Entries => _entries;

    public bool IsReady { get; private set; }

    /// <summary>Listeyi baştan kurar. Zaten kuruluyorsa hemen döner.</summary>
    public void Rebuild(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _building, 1) == 1)
        {
            return;
        }

        try
        {
            var started = DateTime.UtcNow;
            var entries = new List<FileEntry>();
            foreach (var root in _roots().Where(Directory.Exists))
            {
                foreach (var entry in Enumerate(root))
                {
                    ct.ThrowIfCancellationRequested();
                    entries.Add(entry);
                }
            }

            _entries = entries;
            IsReady = true;
            _log.Info($"Dosya indeksi: {entries.Count} öğe, {(DateTime.UtcNow - started).TotalSeconds:F1} sn");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _building, 0);
        }
    }

    /// <summary>Adında aranan metin geçenler; baştan eşleşen ve kısa adlar önde.</summary>
    public IReadOnlyList<(FileEntry Entry, int Score)> Search(string text, int max, CancellationToken ct = default)
    {
        var folded = FuzzyMatcher.Fold(text.Trim());
        if (folded.Length == 0)
        {
            return [];
        }

        var matches = new List<(FileEntry, int)>();
        var entries = _entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var entry = entries[i];
            var index = entry.FoldedName.IndexOf(folded, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var score = 1000 - Math.Min(entry.Name.Length, 500) + (index == 0 ? 500 : 0) + (entry.IsDirectory ? 50 : 0);
            matches.Add((entry, score));
        }

        return matches.OrderByDescending(m => m.Item2).Take(max).ToList();
    }

    private IEnumerable<FileEntry> Enumerate(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };

        return new FileSystemEnumerable<FileEntry>(
            root,
            (ref FileSystemEntry e) => new FileEntry(e.ToFullPath(), e.FileName.ToString(), e.IsDirectory),
            options)
        {
            // Gizli araç klasörleri (.git, .nuget...) ve atlanan adlar listeyi şişirmesin.
            ShouldRecursePredicate = (ref FileSystemEntry e) => !e.FileName.StartsWith(".") && !_skipDirectoryNames.Contains(e.FileName.ToString()),
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.FileName.StartsWith("."),
        };
    }
}
