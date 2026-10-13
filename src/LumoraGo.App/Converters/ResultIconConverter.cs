using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LumoraGo.App.Services;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.App.Converters;

/// <summary>Sonucun simgesi: uygulama simgesi hazırsa resim, değilse eklentiye göre bir Fluent simge karakteri.</summary>
internal sealed class ResultIconConverter : IValueConverter
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

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
        ["lumorago.converter"] = "\uE8AB",
        ["lumorago.notes"] = "\uE70B",
        ["lumorago.emoji"] = "\uE76E",
        ["lumorago.color"] = "\uE790",
        ["lumorago.keywordhints"] = "",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Result result)
        {
            return null;
        }

        if (result.IconPath?.StartsWith(EmojiPlugin.IconPrefix, StringComparison.Ordinal) == true)
        {
            return new TextBlock
            {
                Text = result.IconPath[EmojiPlugin.IconPrefix.Length..],
                FontFamily = EmojiFont,
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        if (result.IconPath?.StartsWith(ColorPlugin.IconPrefix, StringComparison.Ordinal) == true
            && ColorConverter.ConvertFromString(result.IconPath[ColorPlugin.IconPrefix.Length..]) is Color swatch)
        {
            var border = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(swatch),
            };
            border.SetResourceReference(Border.BorderBrushProperty, "SecondaryText");
            return border;
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
