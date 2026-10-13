using LumoraGo.Core.DeepSearch;
using LumoraGo.Core.Files;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class FileSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumorago-fs-" + Guid.NewGuid().ToString("N"));

    public FileSearchTests()
    {
        foreach (var file in new[] { "Rapor.docx", "eski/yıllık rapor 2025.pdf", "node_modules/rapor.js", ".git/rapor", "notlar.txt" })
        {
            var path = Path.Combine(_root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FileIndex BuildIndex()
    {
        var index = new FileIndex(() => [_root], DeepSearchOptions.DefaultSkipDirectoryNames);
        index.Rebuild();
        return index;
    }

    [Fact]
    public void Index_skips_tool_folders_and_ranks_prefix_first()
    {
        var index = BuildIndex();

        var names = index.Search("rapor", 10).Select(m => m.Entry.Name).ToList();

        Assert.Equal(["Rapor.docx", "yıllık rapor 2025.pdf"], names);
        Assert.Equal(["yıllık rapor 2025.pdf"], index.Search("yillik", 10).Select(m => m.Entry.Name));
    }

    [Fact]
    public async Task Plugin_opens_and_reveals_files()
    {
        var opened = new List<string>();
        var revealed = new List<string>();
        var host = new NullHost();
        var plugin = new FileSearchPlugin(BuildIndex(), opened.Add, revealed.Add);
        plugin.Init(new PluginContext(FileSearchPlugin.Metadata, _root, host));

        var result = (await plugin.QueryAsync(new Query("/f rapor", "/f", "rapor"), CancellationToken.None))[0];
        result.Action(new ActionContext(ActionModifiers.None));
        result.Action(new ActionContext(ActionModifiers.Ctrl));
        result.ContextMenu!.Single(a => a.Title == "Yolu kopyala").Action(new ActionContext(ActionModifiers.None));

        var expected = Path.Combine(_root, "Rapor.docx");
        Assert.Equal([expected], opened);
        Assert.Equal([expected], revealed);
        Assert.Equal([expected], host.Copied);
    }
}

public class DeepSearchSkipSetTests
{
    [Fact]
    public void Default_skips_system_folders_and_include_system_keeps_only_unreadable_ones()
    {
        var normal = DeepSearchOptions.BuildSkipSet(false, ["Yedek"]);
        Assert.Contains("Windows", normal);
        Assert.Contains("AppData", normal);
        Assert.Contains("Yedek", normal);

        var everything = DeepSearchOptions.BuildSkipSet(true, ["Yedek"]);
        Assert.DoesNotContain("Windows", everything);
        Assert.DoesNotContain("Program Files", everything);
        Assert.DoesNotContain("node_modules", everything);
        Assert.Contains("$Recycle.Bin", everything);
        Assert.Contains("System Volume Information", everything);
        Assert.Contains("yedek", everything);
    }
}
