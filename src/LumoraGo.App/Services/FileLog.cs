using System.IO;
using LumoraGo.Core;
using LumoraGo.Plugin;

namespace LumoraGo.App.Services;

/// <summary>%LocalAppData%\LumoraGo\logs altına günlük dosyası. Yazma arka planda yapılır, UI thread'i beklemez.</summary>
internal sealed class FileLog : ILog, IDisposable
{
    private readonly System.Threading.Channels.Channel<string> _queue =
        System.Threading.Channels.Channel.CreateUnbounded<string>(new() { SingleReader = true });

    private readonly Task _writer;

    public FileLog(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _writer = Task.Run(WriteLoopAsync);
    }

    public string Directory { get; }

    public void Write(LogLevel level, string message)
    {
#if !DEBUG
        if (level == LogLevel.Debug)
        {
            return;
        }
#endif
        _queue.Writer.TryWrite($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var line in _queue.Reader.ReadAllAsync())
        {
            try
            {
                var path = Path.Combine(Directory, $"lumorago-{DateTime.Now:yyyyMMdd}.log");
                await File.AppendAllTextAsync(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Günlük yazılamıyorsa uygulama yine de çalışmaya devam eder.
            }
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(1));
    }
}
