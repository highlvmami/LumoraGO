using System.Runtime.InteropServices;
using System.Windows;
using LumoraGo.Core;
using LumoraGo.Plugin;

namespace LumoraGo.App.Services;

/// <summary>Eklentilerin launcher'a eriştiği <see cref="IPluginHost"/>. Çağrılar UI thread'ine taşınır.</summary>
internal sealed class LauncherHost(MainWindow window, TrayIcon tray, ILog log) : IPluginHost
{
    public void ChangeQuery(string text) => window.Dispatcher.BeginInvoke(() => window.SetQuery(text));

    public void RefreshResults(string keyword) => window.Dispatcher.BeginInvoke(() => window.RefreshResults(keyword));

    public void HideLauncher() => window.Dispatcher.BeginInvoke(window.HideLauncher);

    public void CopyToClipboard(string text) => window.Dispatcher.BeginInvoke(() =>
    {
        // Pano başka bir uygulamada kısa süre kilitli olabilir; birkaç kez dene.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
        }

        log.Warn("Panoya kopyalanamadı.");
    });

    public void ShowMessage(string title, string message) =>
        window.Dispatcher.BeginInvoke(() => tray.ShowBalloon(title, message));

    public void Log(LogLevel level, string message) => log.Write(level, message);
}
