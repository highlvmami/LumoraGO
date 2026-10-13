using System.Drawing;
using System.Windows.Forms;

namespace LumoraGo.App.Services;

/// <summary>Sistem tepsisi simgesi ve sağ tık menüsü.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _startupItem;

    public TrayIcon(Action show, Action openSettings, Action openLogs, Action exit)
    {
        _startupItem = new ToolStripMenuItem("Windows açılışında başlat") { CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startupItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => show());
        menu.Items.Add("Ayarlar", null, (_, _) => openSettings());
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

    /// <summary>LumoraGo logosu, tepsinin küçük simge boyutunda (bulanık ölçekleme olmasın diye .ico'nun doğru boyutu).</summary>
    private static Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LumoraGo.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException)
        {
            // Aşağıdaki exe simgesine düş.
        }

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
