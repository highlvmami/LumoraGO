using System.Text.Json;
using System.Text.Json.Serialization;
using LumoraGo.Core.Plugins;

namespace LumoraGo.Core.Settings;

public enum ThemeChoice
{
    Dark,
    Light,
    System,
}

/// <summary>Ayarlar penceresinden değişen kullanıcı ayarları.</summary>
public sealed record LauncherSettings
{
    public string Hotkey { get; init; } = "Alt+Space";

    public ThemeChoice Theme { get; init; } = ThemeChoice.Dark;

    /// <summary>"/hava" yalnız yazılınca gösterilecek şehir. Boşsa IP'den bulunan konum.</summary>
    public string WeatherCity { get; init; } = "";

    /// <summary>Launcher her açıldığında hızlı önizleme paneli de açık gelsin.</summary>
    public bool PreviewAlwaysOpen { get; init; }

    /// <summary>Kopyalanan metinler "/c" için hatırlansın mı.</summary>
    public bool ClipboardHistory { get; init; } = true;

    /// <summary>Pano geçmişi LumoraGo kapanınca da kalsın mı (diske yazılır).</summary>
    public bool ClipboardPersist { get; init; }

    /// <summary>"/ds"nin arayacağı klasörler. Boşsa tüm sabit diskler.</summary>
    public IReadOnlyList<string> DeepSearchFolders { get; init; } = [];

    /// <summary>Varsayılanlara ek olarak derin aramada girilmeyecek klasör adları.</summary>
    public IReadOnlyList<string> DeepSearchSkipNames { get; init; } = [];

    /// <summary>"/ds" Windows, Program Files, AppData, node_modules ve .git gibi klasörlere de girsin (yavaş ama eksiksiz).</summary>
    public bool DeepSearchIncludeSystem { get; init; }

    /// <summary>"/f"nin varsayılan kullanıcı klasörlerine ek olarak tarayacağı klasörler.</summary>
    public IReadOnlyList<string> FileSearchFolders { get; init; } = [];

    /// <summary>Kullanıcının eklediği kelime → site/dosya/uygulama kısayolları.</summary>
    public IReadOnlyList<CustomShortcut> CustomShortcuts { get; init; } = [];

    /// <summary>Eylem zincirleri, "[ad]" başlığı ve "tür | değer" satırlarıyla (bkz. ChainText).</summary>
    public string ScriptChains { get; init; } = "";
}

/// <summary>Ayarları JSON dosyasında tutar. Dosya bozuksa varsayılanlarla açılır.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILog _log;

    public SettingsStore(string path, ILog? log = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
        Current = Load();
    }

    public LauncherSettings Current { get; private set; }

    public event EventHandler<LauncherSettings>? Changed;

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
        Current = settings;
        Changed?.Invoke(this, settings);
    }

    private LauncherSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_path), Json) ?? new LauncherSettings()
                : new LauncherSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Ayarlar okunamadı, varsayılanlar kullanılıyor: {ex.Message}");
            return new LauncherSettings();
        }
    }
}
