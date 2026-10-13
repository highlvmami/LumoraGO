using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Extensions;

/// <summary>Eklenti klasöründeki plugin.json dosyası.</summary>
/// <param name="Assembly">Yüklenecek DLL'in klasöre göre yolu; boşsa klasördeki tek DLL ya da klasör adıyla aynı olan.</param>
/// <param name="Type">IPlugin sınıfının tam adı; boşsa DLL'deki ilk IPlugin sınıfı.</param>
public sealed record ExternalPluginManifest(string Id, string Name, string? Keyword = null, string? Assembly = null, string? Type = null);

/// <summary>
/// "%LOCALAPPDATA%\LumoraGo\extensions" altındaki her klasörü bir eklenti olarak yükler. Her eklenti kendi
/// <see cref="AssemblyLoadContext"/>'inde çalışır; böylece bağımlılıkları birbiriyle ve LumoraGo ile çakışmaz.
/// Sorunlu bir eklenti günlüğe yazılıp atlanır, diğerleri ve launcher etkilenmez.
/// </summary>
public static class ExternalPlugins
{
    public const string ManifestFileName = "plugin.json";

    /// <summary>Yerleşik eklentilerin kimlik öneki; dış eklentiler kullanamaz.</summary>
    public const string ReservedIdPrefix = "lumorago.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<PluginEntry> Load(string root, ILog? log = null)
    {
        log ??= NullLog.Instance;
        if (!Directory.Exists(root))
        {
            return [];
        }

        var entries = new List<PluginEntry>();
        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (LoadFolder(folder, log) is { } entry)
                {
                    entries.Add(entry);
                    log.Info($"Eklenti yüklendi: {entry.Metadata.Id} ({folder})");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Eklenti yüklenemedi: {folder}: {ex}");
            }
        }

        return entries;
    }

    private static PluginEntry? LoadFolder(string folder, ILog log)
    {
        var manifestPath = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            log.Warn($"Eklenti klasöründe {ManifestFileName} yok, atlandı: {folder}");
            return null;
        }

        var manifest = JsonSerializer.Deserialize<ExternalPluginManifest>(File.ReadAllText(manifestPath), Json);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
        {
            log.Warn($"{manifestPath}: \"id\" ve \"name\" gerekli, atlandı.");
            return null;
        }

        if (manifest.Id.StartsWith(ReservedIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"{manifestPath}: \"{ReservedIdPrefix}\" ile başlayan kimlikler LumoraGo'ya ayrılmış, atlandı.");
            return null;
        }

        var keyword = string.IsNullOrWhiteSpace(manifest.Keyword) ? null : manifest.Keyword.Trim();
        if (keyword is not null && (!keyword.StartsWith('/') || keyword.Contains(' ')))
        {
            log.Warn($"{manifestPath}: \"keyword\" \"/\" ile başlamalı ve boşluk içermemeli, atlandı.");
            return null;
        }

        var dllPath = FindAssembly(folder, manifest);
        if (dllPath is null)
        {
            log.Warn($"{manifestPath}: yüklenecek DLL bulunamadı, atlandı.");
            return null;
        }

        var context = new PluginLoadContext(dllPath);
        var assembly = context.LoadFromAssemblyPath(dllPath);
        var type = manifest.Type is { Length: > 0 }
            ? assembly.GetType(manifest.Type, throwOnError: true)!
            : assembly.GetExportedTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });
        if (type is null || !typeof(IPlugin).IsAssignableFrom(type))
        {
            log.Warn($"{dllPath}: IPlugin uygulayan public bir sınıf yok, atlandı.");
            return null;
        }

        var plugin = (IPlugin)Activator.CreateInstance(type)!;
        return new PluginEntry(new PluginMetadata(manifest.Id.Trim(), manifest.Name.Trim(), keyword), plugin);
    }

    private static string? FindAssembly(string folder, ExternalPluginManifest manifest)
    {
        if (manifest.Assembly is { Length: > 0 })
        {
            var full = Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
            // plugin.json klasör dışını göstermesin.
            var inside = full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return inside && File.Exists(full) ? full : null;
        }

        var named = Path.Combine(folder, Path.GetFileName(folder) + ".dll");
        if (File.Exists(named))
        {
            return named;
        }

        var dlls = Directory.GetFiles(folder, "*.dll")
            .Where(p => !SharedAssembly(Path.GetFileNameWithoutExtension(p)))
            .ToList();
        return dlls.Count == 1 ? dlls[0] : null;
    }

    /// <summary>Bu derlemeler LumoraGo ile paylaşılır; eklentinin kendi kopyası yüklenirse IPlugin türü eşleşmez.</summary>
    private static bool SharedAssembly(string? name) =>
        string.Equals(name, typeof(IPlugin).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);

    private sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath))
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);
        private readonly string _folder = Path.GetDirectoryName(mainAssemblyPath)!;

        protected override Assembly? Load(AssemblyName name)
        {
            if (SharedAssembly(name.Name))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(name);
            if (path is null && name.Name is { } simple)
            {
                // .deps.json olmadan kopyalanmış eklentiler için klasöre bak.
                var candidate = Path.Combine(_folder, simple + ".dll");
                path = File.Exists(candidate) ? candidate : null;
            }

            // Bulunamayan (çoğunlukla .NET'in kendi) derlemeler varsayılan bağlamdan gelir.
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
