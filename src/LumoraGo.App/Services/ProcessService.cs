using System.ComponentModel;
using System.Diagnostics;
using LumoraGo.Core.Desktop;

namespace LumoraGo.App.Services;

/// <summary>"/kill" için kullanıcının oturumundaki işlemleri listeler ve sonlandırır.</summary>
internal static class ProcessService
{
    /// <summary>Kapatılırsa Windows'u bozabilecek işlemler listelenmez.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "Memory Compression", "explorer", "sihost", "ctfmon", "conhost", "RuntimeBroker",
    };

    public static IReadOnlyList<RunningProcess> List()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var own = Environment.ProcessId;
        var list = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == own || process.SessionId != session || Protected.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    string? exe = null;
                    try
                    {
                        exe = process.MainModule?.FileName;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        // Yükseltilmiş işlemlerin yolu okunamaz; simgesiz gösterilir.
                    }

                    list.Add(new RunningProcess(process.Id, process.ProcessName, process.WorkingSet64, exe));
                }
                catch (InvalidOperationException)
                {
                    // Listelerken kapanmış.
                }
            }
        }

        return list;
    }

    public static int Kill(IReadOnlyList<RunningProcess> processes)
    {
        var killed = 0;
        foreach (var info in processes)
        {
            try
            {
                using var process = Process.GetProcessById(info.Id);
                if (!string.Equals(process.ProcessName, info.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Kimlik başka bir işleme geçmiş.
                }

                process.Kill();
                killed++;
            }
            catch (Exception ex) when (ex is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Zaten kapanmış ya da yetki yok (yönetici olarak çalışan uygulamalar).
            }
        }

        return killed;
    }
}
