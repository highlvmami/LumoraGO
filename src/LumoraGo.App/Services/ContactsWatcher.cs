using System.IO;
using LumoraGo.Core;
using LumoraGo.Core.Contacts;

namespace LumoraGo.App.Services;

/// <summary>Ayar açıksa kişi dosyası değişince rehberi yeniden içe aktarır. Kaydederken birkaç olay arka arkaya gelir; 2 sn beklenir.</summary>
internal sealed class ContactsWatcher(ContactBook book, ILog log) : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private string _file = "";

    public void Watch(string file, bool enabled)
    {
        if (enabled && file == _file && _watcher is not null)
        {
            return;
        }

        Stop();
        if (!enabled || file.Length == 0 || Path.GetDirectoryName(file) is not { Length: > 0 } dir || !Directory.Exists(dir))
        {
            return;
        }

        _file = file;
        _timer = new Timer(_ => Reimport(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => _timer?.Change(Settle, Timeout.InfiniteTimeSpan);
        _watcher.Created += (_, _) => _timer?.Change(Settle, Timeout.InfiniteTimeSpan);
        _watcher.Renamed += (_, _) => _timer?.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    private void Reimport()
    {
        try
        {
            var count = book.Import(_file);
            log.Info($"Kişi dosyası değişti, {count} kişi yeniden aktarıldı.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warn($"Kişi dosyası yeniden okunamadı: {ex.Message}");
        }
    }

    private void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _timer?.Dispose();
        _timer = null;
        _file = "";
    }

    public void Dispose() => Stop();
}
