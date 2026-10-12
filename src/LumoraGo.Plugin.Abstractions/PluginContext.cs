namespace LumoraGo.Plugin;

/// <param name="Id">Benzersiz kimlik, örn. "lumorago.calculator".</param>
/// <param name="Name">Kullanıcıya görünen ad.</param>
/// <param name="ActionKeyword">Tetik kelimesi. null ise eklenti genel aramaya katılır.</param>
public sealed record PluginMetadata(string Id, string Name, string? ActionKeyword = null);

/// <summary>Eklentinin <see cref="IPlugin.Init"/> sırasında aldığı bağlam.</summary>
/// <param name="Metadata">Eklentinin kendi bilgileri.</param>
/// <param name="DataDirectory">Eklentinin ayar ve önbellek yazabileceği klasör.</param>
/// <param name="Host">Launcher'ı yönetmek için kullanılan API.</param>
public sealed record PluginContext(PluginMetadata Metadata, string DataDirectory, IPluginHost Host);

/// <summary>Eklentilerin launcher'a erişim noktası. Eklenti UI'a doğrudan dokunmaz, bu API'yi kullanır.</summary>
public interface IPluginHost
{
    /// <summary>Arama kutusundaki metni değiştirir.</summary>
    void ChangeQuery(string text);

    /// <summary>Launcher penceresini gizler.</summary>
    void HideLauncher();

    /// <summary>Metni panoya kopyalar.</summary>
    void CopyToClipboard(string text);

    /// <summary>Kullanıcıya kısa bir bildirim gösterir.</summary>
    void ShowMessage(string title, string message);

    void Log(LogLevel level, string message);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}
