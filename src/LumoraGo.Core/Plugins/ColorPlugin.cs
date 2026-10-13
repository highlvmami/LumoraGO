using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>Bir renk kodu yazılınca HEX, RGB ve HSL karşılıklarını gösterir; Enter seçileni kopyalar.</summary>
public sealed class ColorPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.color", "Renk kodları");

    /// <summary>Sonuç simgesini renk örneği olarak çizdirmek için <see cref="Result.IconPath"/> öneki; ardından "#RRGGBB" gelir.</summary>
    public const string IconPrefix = "color:";

    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!ColorCode.TryParse(query.Search, out var color))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var swatch = IconPrefix + ColorCode.ToHex(color with { A = 1 });
        var formats = new (string Text, string Label)[]
        {
            (ColorCode.ToHex(color), "HEX"),
            (ColorCode.ToRgb(color), "RGB"),
            (ColorCode.ToHsl(color), "HSL"),
        };

        var results = formats
            .Select((f, i) => new Result(f.Text, $"{f.Label} · Enter ile kopyala", swatch, CalculatorPlugin.ResultScore - i, _ =>
            {
                _host?.CopyToClipboard(f.Text);
                return true;
            }))
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }
}
