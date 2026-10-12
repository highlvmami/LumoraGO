using System.Drawing;
using System.Windows.Forms;

namespace LumoraGo.App.Services;

/// <summary>Sistem tepsisi simgesi ve sağ tık menüsü.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _startupItem;

    public TrayIcon(Action show, Action openLogs, Action exit)
    {
        _startupItem = new ToolStripMenuItem("Windows açılışında başlat") { CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startupItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => show());
        menu.Items.Add(_startupItem);
        menu.Items.Add("Günlük klasörünü aç", null, (_, _) => openLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => exit());
        menu.Opening += (_, _) => _startupItem.Checked = StartupRegistration.IsEnabled;

        _icon = new NotifyIcon
        {
            Text = "LumoraGo",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                show();
            }
        };
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _icon.ShowBalloonTip(3000, title, text, icon);
    }

    private static Icon LoadIcon()
    {
        // Faz 5'te kendi simgemiz gelene kadar uygulamanın exe simgesi
        return Environment.ProcessPath is { } path
            ? Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application
            : SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
