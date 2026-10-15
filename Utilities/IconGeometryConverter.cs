using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OrbitLauncher.Utilities;

public sealed class IconGeometryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Geometry geometry)
            return geometry;

        if (value is string glyph && glyph.Length > 0)
        {
            var codePoint = char.ConvertToUtf32(glyph, 0);
            return Application.Current?.TryFindResource($"Icon{codePoint:X4}") as Geometry ?? Geometry.Empty;
        }

        return Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
