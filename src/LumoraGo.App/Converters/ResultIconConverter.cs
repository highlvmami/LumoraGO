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
        ["lumorago.windows"] = "\uE737",
        ["lumorago.timer"] = "\uE916",
        ["lumorago.translate"] = "\uE8C1",
        ["lumorago.wikipedia"] = "\uE736",
        ["lumorago.maps"] = "\uE707",
        ["lumorago.githubsearch"] = "\uE721",
        ["lumorago.custom"] = "\uE71B",
        ["lumorago.tool.uuid"] = "\uE943",
        ["lumorago.tool.password"] = "\uE8D7",
        ["lumorago.tool.base64"] = "\uE943",
        ["lumorago.tool.url"] = "\uE71B",
        ["lumorago.tool.hash"] = "\uE72E",
        ["lumorago.tool.count"] = "\uE8C8",
        ["lumorago.kill"] = "\uE711",
        ["lumorago.media"] = "\uE767",
        ["lumorago.net.ping"] = "\uE839",
        ["lumorago.net.dns"] = "\uE774",
        ["lumorago.net.whois"] = "\uE946",
        ["lumorago.net.speed"] = "\uEC4A",
        ["lumorago.net.myip"] = "\uE81D",
        ["lumorago.calendar"] = "\uE787",
        ["lumorago.mail"] = "\uE715",
        ["lumorago.bookmarks"] = "\uE734",
        ["lumorago.winsettings"] = "\uE713",
        ["lumorago.recent"] = "\uE81C",
        ["lumorago.clock"] = "\uE12B",
        ["lumorago.dictionary"] = "\uE82D",
        ["lumorago.dictionary.ascii"] = "\uE82D",
        ["lumorago.quote"] = "\uE9D2",
        ["lumorago.weather"] = "\uE753",
        ["lumorago.football"] = "\uE7FC",
        ["lumorago.scripts"] = "\uE756",
        ["lumorago.contacts"] = "\uE77B",
        ["lumorago.contacts.kw"] = "\uE77B",
        ["lumorago.contacts.kw.kişi"] = "\uE77B",
        ["lumorago.scripts.run"] = "\uE756",
        ["lumorago.scripts.run.calistir"] = "\uE756",
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
