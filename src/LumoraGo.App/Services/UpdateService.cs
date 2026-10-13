using System.Reflection;
using LumoraGo.Core;
using Velopack;
using Velopack.Sources;

namespace LumoraGo.App.Services;

/// <summary>
/// Açılışta ve sonra düzenli aralıklarla GitHub Releases'tan yeni sürüm arar ve arka planda indirir. İndirme bitince launcher gizliyse
/// hemen, açıksa gizlendiği an kurup sessizce yeniden başlar.
/// </summary>
internal sealed class UpdateService(ILog log)
{
    private const string RepoUrl = "https://github.com/highlvmami/LumoraGO";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));
    private UpdateInfo? _ready;

    public string Status { get; private set; } = "Güncelleme kontrol ediliyor…";

    /// <summary>Çalışan sürüm. Kurulu değilse (dotnet run, zip) derlemenin sürümü.</summary>
    public string CurrentVersion =>
        _manager.IsInstalled && _manager.CurrentVersion is { } installed
            ? installed.ToString()
            : AssemblyVersion();

    // GitHub yanıt vermezse durum "kontrol ediliyor"da kalmasın.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private bool _busy;

    public async Task CheckAndDownloadAsync()
    {
        if (!_manager.IsInstalled)
        {
            Status = "Otomatik güncelleme kapalı: bu kopya kurulum dosyasıyla yüklenmedi";
            return;
        }

        if (_busy || _ready is not null)
        {
            return;
        }

        _busy = true;
        try
        {
            log.Info("Güncelleme kontrol ediliyor");

            // Velopack çağrıları arayüz iş parçacığının bağlamına takılmasın diye arka planda çalışır.
            var update = await Task.Run(() => _manager.CheckForUpdatesAsync()).WaitAsync(CheckTimeout);
            if (update is null)
            {
                Status = $"Güncel (son kontrol {DateTime.Now:HH:mm})";
                log.Info("Güncel");
                return;
            }

            var version = update.TargetFullRelease.Version;
            Status = $"{version} indiriliyor…";
            log.Info($"Yeni sürüm bulundu: {version}");

            await Task.Run(() => _manager.DownloadUpdatesAsync(update));
            _ready = update;
            Status = $"{version} hazır, launcher kapanınca yüklenecek";
            log.Info($"Güncelleme indirildi: {version}");
        }
        catch (TimeoutException)
        {
            Status = "Güncelleme sunucusu yanıt vermedi, birazdan tekrar denenecek";
            log.Warn($"Güncelleme kontrolü {CheckTimeout.TotalSeconds} saniyede bitmedi");
        }
        catch (Exception ex)
        {
            Status = "Güncelleme kontrol edilemedi (internet bağlantısını kontrol et), birazdan tekrar denenecek";
            log.Warn($"Güncelleme hatası: {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    public bool IsReadyToApply => _ready is not null;

    /// <summary>İndirilen güncellemeyi kurar ve uygulamayı tepside sessizce yeniden başlatır.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is null)
        {
            return;
        }

        log.Info($"Güncelleme kuruluyor: {_ready.TargetFullRelease.Version}");
        _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease, [StartupRegistration.BackgroundArg]);
    }

    private static string AssemblyVersion()
    {
        var informational = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        // "0.2.0+abc123" gibi commit ekini at
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
