using System.Text.Json;

namespace LumoraGo.Core.Accounts;

/// <param name="Host">IMAP sunucusu, örn. imap.gmail.com.</param>
/// <param name="Address">Giriş adı (genelde e-posta adresi).</param>
public sealed record MailAccount(string Host, int Port, string Address, string Password);

/// <summary>Takvim ve mail için gizli tutulan bilgiler. Settings.json'a değil, korumalı dosyaya yazılır.</summary>
public sealed record AccountSecrets
{
    /// <summary>Takvimlerin gizli ICS adresleri (adresi bilen herkes takvimi okuyabilir, bu yüzden gizli).</summary>
    public IReadOnlyList<string> CalendarUrls { get; init; } = [];

    public MailAccount? Mail { get; init; }

    /// <summary>Google Rehber bağlantısı; "Google ile bağlan" yapılmadıysa null.</summary>
    public GoogleAccount? Google { get; init; }

    /// <summary>"Spotify ile bağlan" yapılmadıysa null.</summary>
    public SpotifyAccount? Spotify { get; init; }

    /// <summary>Yapay zekâ (Claude) için kullanıcının kendi API anahtarı.</summary>
    public string? AnthropicApiKey { get; init; }

    /// <summary>"/film" için themoviedb.org API anahtarı (v3 anahtarı ya da okuma erişim anahtarı).</summary>
    public string? TmdbApiKey { get; init; }
}

/// <summary>Kullanıcının Spotify geliştirici panelinde oluşturduğu uygulamanın kimliği ve girişten sonra alınan yenileme anahtarı.</summary>
public sealed record SpotifyAccount(string ClientId, string RefreshToken = "", string Name = "", bool Premium = false);

/// <summary>Kullanıcının Google Cloud'da oluşturduğu masaüstü OAuth istemcisi ve girişten sonra alınan yenileme anahtarı.</summary>
/// <param name="RefreshToken">Boşsa istemci kayıtlı ama giriş yapılmamış.</param>
public sealed record GoogleAccount(string ClientId, string ClientSecret, string RefreshToken = "", string Email = "");

/// <summary>
/// Gizli bilgileri korumalı dosyada tutar. Koruma (Windows'ta DPAPI) dışarıdan verilir, böylece Core platformdan bağımsız kalır.
/// Dosya bozuksa ya da başka kullanıcının anahtarıyla yazılmışsa boş açılır.
/// </summary>
public sealed class SecretStore
{
    private readonly string _path;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;
    private readonly ILog _log;

    public SecretStore(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, ILog? log = null)
    {
        _path = path;
        _protect = protect;
        _unprotect = unprotect;
        _log = log ?? NullLog.Instance;
        Current = Load();
    }

    public AccountSecrets Current { get; private set; }

    public event EventHandler<AccountSecrets>? Changed;

    public void Save(AccountSecrets secrets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, _protect(JsonSerializer.SerializeToUtf8Bytes(secrets)));
        File.Move(temp, _path, overwrite: true);
        Current = secrets;
        Changed?.Invoke(this, secrets);
    }

    private AccountSecrets Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AccountSecrets>(_unprotect(File.ReadAllBytes(_path))) ?? new AccountSecrets()
                : new AccountSecrets();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _log.Warn($"Gizli bilgiler okunamadı, boş açılıyor: {ex.Message}");
            return new AccountSecrets();
        }
    }
}
