using LumoraGo.Plugin;

namespace LumoraGo.SamplePlugin;

/// <summary>"/merhaba Ayşe" yazınca "Merhaba, Ayşe!" gösterir; Enter kopyalar. Kendi eklentin için başlangıç noktası.</summary>
public sealed class HelloPlugin : IPlugin
{
    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var name = query.Search.Trim();
        var text = name.Length == 0 ? "Merhaba!" : $"Merhaba, {name}!";
        var result = new Result(text, "Enter ile kopyala", null, 1, _ =>
        {
            _host?.CopyToClipboard(text);
            return true;
        });

        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }
}
