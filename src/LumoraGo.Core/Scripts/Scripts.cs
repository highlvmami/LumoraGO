using System.Diagnostics;
using System.Text;

namespace LumoraGo.Core.Scripts;

public sealed record ScriptItem(string Name, string Path)
{
    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
}

public enum ScriptMode
{
    /// <summary>Pencere açmadan çalışır, bitince bildirim.</summary>
    Background,

    /// <summary>Terminal penceresinde görünür çalışır ve pencere açık kalır.</summary>
    Visible,

    /// <summary>Yönetici olarak, görünür.</summary>
    Admin,
}

/// <param name="Output">Çıktının son satırları (stdout ve stderr birlikte).</param>
public sealed record ScriptRun(int ExitCode, string Output);

/// <summary>Betik klasöründeki (.ps1, .bat, .cmd, .py) dosyalar.</summary>
public sealed class ScriptCatalog(Func<string> folder)
{
    public static readonly string[] Extensions = [".ps1", ".bat", ".cmd", ".py"];

    public string Folder => folder();

    public IReadOnlyList<ScriptItem> List()
    {
        try
        {
            var dir = folder();
            if (!Directory.Exists(dir))
            {
                return [];
            }

            return Directory.EnumerateFiles(dir)
                .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => new ScriptItem(System.IO.Path.GetFileNameWithoutExtension(f), f))
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Klasör yoksa oluşturur ve içine örnek betiği yazar; varsa dokunmaz.</summary>
    /// <returns>Klasör bu çağrıda oluşturulduysa true.</returns>
    public bool EnsureFolder()
    {
        var dir = folder();
        if (Directory.Exists(dir))
        {
            return false;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllText(System.IO.Path.Combine(dir, "merhaba.ps1"), SampleScript, new UTF8Encoding(true));
        return true;
    }

    public const string SampleScript =
        "# LumoraGo örnek betiği. Alt+Space ile \"merhaba\" yaz ve Enter'a bas.\r\n" +
        "# Adından sonra yazdıkların argüman olarak gelir: \"merhaba Ayşe\".\r\n" +
        "param([string]$Ad = \"dünya\")\r\n" +
        "Write-Output \"Merhaba, $Ad!\"\r\n";

    /// <summary>"yedekle D:\\" gibi bir metinden betiği ve argümanları ayırır; en uzun eşleşen ad kazanır.</summary>
    public (ScriptItem Script, string Arguments)? Resolve(string text)
    {
        text = text.Trim();
        foreach (var script in List().OrderByDescending(s => s.Name.Length))
        {
            if (string.Equals(text, script.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (script, "");
            }

            if (text.Length > script.Name.Length && text[script.Name.Length] == ' '
                && text.StartsWith(script.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (script, text[(script.Name.Length + 1)..].Trim());
            }
        }

        return null;
    }

    public ScriptItem? Find(string name) =>
        List().FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Betikleri türüne göre doğru yorumlayıcıyla başlatır. PowerShell yalnızca bu çalıştırma için ExecutionPolicy Bypass alır.</summary>
public sealed class ScriptRunner(Func<ProcessStartInfo, CancellationToken, Task<ScriptRun>>? execute = null)
{
    public const int OutputLines = 6;

    private readonly Func<ProcessStartInfo, CancellationToken, Task<ScriptRun>> _execute = execute ?? ExecuteAsync;

    public static ProcessStartInfo StartInfo(ScriptItem script, string arguments, ScriptMode mode)
    {
        var path = Quote(script.Path);
        var args = arguments.Trim();
        var keepOpen = mode != ScriptMode.Background;
        var (file, line) = script.Extension switch
        {
            ".ps1" => ("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass {(keepOpen ? "-NoExit " : "")}-File {path} {args}"),
            ".py" when keepOpen => ("cmd.exe", $"/k py {path} {args}"),
            ".py" => ("py.exe", $"{path} {args}"),
            _ => ("cmd.exe", $"{(keepOpen ? "/k" : "/c")} \"{path} {args}\""),
        };

        var info = new ProcessStartInfo(file, line.TrimEnd())
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(script.Path) ?? "",
        };

        if (mode == ScriptMode.Admin)
        {
            info.UseShellExecute = true;
            info.Verb = "runas";
        }
        else if (mode == ScriptMode.Background)
        {
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }
        else
        {
            info.UseShellExecute = false;
            info.CreateNoWindow = false;
        }

        return info;
    }

    /// <summary>Arka planda çalıştırır ve bitmesini bekler.</summary>
    public Task<ScriptRun> RunAsync(ScriptItem script, string arguments, CancellationToken ct) =>
        _execute(StartInfo(script, arguments, ScriptMode.Background), ct);

    /// <summary>Görünür ya da yönetici olarak başlatır, beklemez.</summary>
    public static void Launch(ScriptItem script, string arguments, ScriptMode mode) =>
        Process.Start(StartInfo(script, arguments, mode))?.Dispose();

    public static async Task<ScriptRun> ExecuteAsync(ProcessStartInfo info, CancellationToken ct)
    {
        using var process = new Process { StartInfo = info };
        var lines = new Queue<string>();
        void Keep(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (lines)
            {
                lines.Enqueue(line.TrimEnd());
                while (lines.Count > OutputLines)
                {
                    lines.Dequeue();
                }
            }
        }

        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Zaten bitti.
            }

            throw;
        }

        lock (lines)
        {
            return new ScriptRun(process.ExitCode, string.Join("\n", lines));
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
