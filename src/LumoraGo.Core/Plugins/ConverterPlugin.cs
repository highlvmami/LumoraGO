using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"5 km mi", "100 usd try", "30 c f": birim ve döviz çevirir; Enter sonucu kopyalar.</summary>
public sealed class ConverterPlugin : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.converter", "Birim ve döviz çevirme");

    private readonly CurrencyRates _rates;
    private readonly Func<DateTime> _utcNow;
    private IPluginHost? _host;

    public ConverterPlugin(CurrencyRates rates, Func<DateTime>? utcNow = null)
    {
        _rates = rates;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (!UnitConverter.TryParse(query.Search, _rates.IsKnown, out var request))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        if (request.Kind != UnitKind.Currency)
        {
            var value = UnitConverter.Convert(request);
            return Task.FromResult<IReadOnlyList<Result>>([Row(
                value,
                UnitConverter.Display(request.To),
                $"{UnitConverter.FormatNumber(request.Amount)} {UnitConverter.Display(request.From)} · Enter ile kopyala")]);
        }

        _ = _rates.RefreshIfStaleAsync(_utcNow());
        if (!_rates.TryConvert(request.Amount, request.From, request.To, out var converted))
        {
            return Task.FromResult<IReadOnlyList<Result>>(
                [new Result("Döviz kurları yükleniyor…", "İnternet bağlantısı gerekiyor; birkaç saniye sonra tekrar yaz", null, CalculatorPlugin.ResultScore, _ => false)]);
        }

        var updated = _rates.UpdatedUtc is { } u ? $" · kur: {u.ToLocalTime():dd.MM HH:mm}" : "";
        return Task.FromResult<IReadOnlyList<Result>>([Row(
            converted,
            request.To,
            $"{UnitConverter.FormatNumber(request.Amount)} {request.From}{updated} · Enter ile kopyala")]);
    }

    private Result Row(double value, string unit, string subtitle)
    {
        var copy = UnitConverter.FormatNumber(value, grouping: false);
        return new Result($"= {UnitConverter.FormatNumber(value)} {unit}", subtitle, null, CalculatorPlugin.ResultScore, _ =>
        {
            _host?.CopyToClipboard(copy);
            return true;
        })
        {
            AutoComplete = copy,
        };
    }
}
