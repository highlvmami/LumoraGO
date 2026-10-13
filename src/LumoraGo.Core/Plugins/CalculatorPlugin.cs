using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"2+3*4" gibi bir hesap yazılınca sonucu en üstte gösterir; Enter sonucu panoya kopyalar.</summary>
public sealed class CalculatorPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.calculator", "Hesap makinesi");

    /// <summary>Hesap sonucu uygulama sonuçlarının üstünde dursun.</summary>
    public const int ResultScore = 1000;

    private IPluginHost? _host;

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!Calculator.TryEvaluate(query.Search, out var value))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var display = Calculator.Format(value);
        var copyText = Calculator.Format(value, grouping: false);
        var result = new Result(
            $"= {display}",
            "Enter ile sonucu kopyala",
            null,
            ResultScore,
            _ =>
            {
                _host?.CopyToClipboard(copyText);
                return true;
            })
        {
            // Tab sonucu kutuya yazar; hesaba oradan devam edilebilir.
            AutoComplete = copyText,
            DragData = DragPayload.Text(copyText),
        };

        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }
}
