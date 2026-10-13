using LumoraGo.Core.Extensions;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class ExternalPluginTests
{
    private static string SampleFolder => Path.Combine(AppContext.BaseDirectory, "extensions", "merhaba");

    [Fact]
    public async Task Loads_sample_plugin_in_its_own_context()
    {
        var root = TempRoot();
        try
        {
            CopyDirectory(SampleFolder, Path.Combine(root, "merhaba"));
            var log = new ListLog();

            var entry = Assert.Single(ExternalPlugins.Load(root, log));
            Assert.Equal(new PluginMetadata("ornek.merhaba", "Merhaba (örnek eklenti)", "/merhaba"), entry.Metadata);
            Assert.NotSame(
                System.Runtime.Loader.AssemblyLoadContext.Default,
                System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(entry.Plugin.GetType().Assembly));

            var host = new NullHost();
            var manager = new PluginManager(log);
            manager.Initialize([entry], host, root);
            var result = Assert.Single(await entry.Plugin.QueryAsync(new Query("/merhaba Ayşe", "/merhaba", "Ayşe"), CancellationToken.None));
            Assert.Equal("Merhaba, Ayşe!", result.Title);
            result.Action(new ActionContext(ActionModifiers.None));
            Assert.Equal(["Merhaba, Ayşe!"], host.Copied);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{ "name": "Kimliksiz" }""")]
    [InlineData("""{ "id": "lumorago.sahte", "name": "Yerleşik taklidi" }""")]
    [InlineData("""{ "id": "x", "name": "Boşluklu", "keyword": "/a b" }""")]
    [InlineData("""{ "id": "x", "name": "Dışarı", "assembly": "../dis.dll" }""")]
    [InlineData("bozuk json")]
    public void Skips_bad_manifests_without_throwing(string manifest)
    {
        var root = TempRoot();
        try
        {
            var folder = Path.Combine(root, "merhaba");
            CopyDirectory(SampleFolder, folder);
            File.WriteAllText(Path.Combine(folder, ExternalPlugins.ManifestFileName), manifest);
            var log = new ListLog();

            Assert.Empty(ExternalPlugins.Load(root, log));
            Assert.Contains(log.Entries, e => e.Level is LogLevel.Warning or LogLevel.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_root_or_manifest_is_fine()
    {
        Assert.Empty(ExternalPlugins.Load(Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}")));

        var root = TempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bos"));
            Assert.Empty(ExternalPlugins.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumorago-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }
    }
}
