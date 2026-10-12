using System.Diagnostics;
using System.IO;
using LumoraGo.Core.Apps;

namespace LumoraGo.App.Services;

internal sealed class ShellAppLauncher : IAppLauncher
{
    public void Launch(AppEntry app, LaunchMode mode)
    {
        if (mode.HasFlag(LaunchMode.OpenFolder))
        {
            OpenFolder(app);
            return;
        }

        if (app.Kind == AppKind.Packaged)
        {
            // Store uygulamaları exe yoluyla değil, kabuk üzerinden AUMID ile açılır.
            Process.Start(new ProcessStartInfo("explorer.exe", app.LaunchPath) { UseShellExecute = true });
            return;
        }

        var start = new ProcessStartInfo(app.LaunchPath) { UseShellExecute = true };
        if (mode.HasFlag(LaunchMode.AsAdmin))
        {
            start.Verb = "runas";
        }

        Process.Start(start);
    }

    private static void OpenFolder(AppEntry app)
    {
        var file = app.TargetPath is { } target && File.Exists(target) ? target : app.LaunchPath;
        var arguments = app.Kind == AppKind.Packaged ? "shell:AppsFolder" : $"/select,\"{file}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }
}
