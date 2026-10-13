using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LumoraGo.App.Services;
using LumoraGo.Plugin;

namespace LumoraGo.App.Converters;

/// <summary>Sonucun simgesi: uygulama simgesi hazırsa resim, değilse eklentiye göre bir Fluent simge karakteri.</summary>
internal sealed class ResultIconConverter : IValueConverter
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["lumorago.calculator"] = "",
        ["lumorago.commands"] = "",
        ["lumorago.commandlist"] = "",
        ["lumorago.version"] = "",
        ["lumorago.apps"] = "",
        ["lumorago.youtube"] = "",
        ["lumorago.google"] = "",
        ["lumorago.deepsearch"] = "",
        ["lumorago.files"] = "",
        ["lumorago.clipboard"] = "",
        ["lumorago.github"] = "",
        ["lumorago.linkedin"] = "",
        ["lumorago.linkedln"] = "",
        ["lumorago.keywordhints"] = "",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Result result)
        {
            return null;
        }

        if (IconCache.Get(result.IconPath) is { } icon)
        {
            return new Image { Source = icon, Width = 28, Height = 28 };
        }

        var glyph = new TextBlock
        {
            Text = Glyphs.GetValueOrDefault(result.PluginId, ""),
            FontFamily = IconFont,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return glyph;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
