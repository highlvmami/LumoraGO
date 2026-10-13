using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LumoraGo.App.Converters;

/// <summary>Değer null ise Collapsed (parametre "invert" ise tersi). Bilgi kartı ile düz satır arasında seçim için.</summary>
internal sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (parameter as string == "invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Kartın eğri değerlerini 140×44'lük bir kutuya ölçekli noktalara çevirir (en yüksek değer üstte).</summary>
internal sealed class TrendPointsConverter : IValueConverter
{
    public const double Width = 140;
    public const double Height = 44;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var points = new PointCollection();
        if (value is not IReadOnlyList<double> { Count: > 1 } values)
        {
            return points;
        }

        var min = values.Min();
        var max = values.Max();
        var span = max - min;
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * Width / (values.Count - 1);
            var y = span <= 0 ? Height / 2 : Height - ((values[i] - min) / span * (Height - 4)) - 2;
            points.Add(new Point(x, y));
        }

        points.Freeze();
        return points;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Kartın yönüne göre renk: artış yeşil, düşüş kırmızı, nötr vurgu rengi.</summary>
internal sealed class TrendBrushConverter : IValueConverter
{
    private static readonly Brush Up = Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly Brush Down = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        true => Up,
        false => Down,
        _ => Application.Current?.TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
