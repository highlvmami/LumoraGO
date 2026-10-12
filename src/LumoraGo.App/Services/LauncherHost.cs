using LumoraGo.Core;
using LumoraGo.Plugin;

namespace LumoraGo.App.Services;

/// <summary>Eklentilerin launcher'a eriştiği <see cref="IPluginHost"/>. Çağrılar UI thread'ine taşınır.</summary>
internal sealed class LauncherHost(MainWindow window, TrayIcon tray, ILog log) : IPluginHost
{
    public void ChangeQuery(string text) => window.Dispatcher.BeginInvoke(() => window.SetQuery(text));

    public void HideLauncher() => window.Dispatcher.BeginInvoke(window.HideLauncher);

    public void ShowMessage(string title, string message) =>
        window.Dispatcher.BeginInvoke(() => tray.ShowBalloon(title, message));

    public void Log(LogLevel level, string message) => log.Write(level, message);
}
