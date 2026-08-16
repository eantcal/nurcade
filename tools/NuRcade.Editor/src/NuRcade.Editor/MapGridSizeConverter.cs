using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NuRcade.Editor;

public sealed class MapGridSizeConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var availableWidth = ReadDouble(values.ElementAtOrDefault(0), 640.0);
        var columnCount = Math.Max(1, (int)ReadDouble(values.ElementAtOrDefault(1), 1.0));
        var zoom = ReadDouble(values.ElementAtOrDefault(2), 1.0);

        var cellSize = Math.Clamp((availableWidth / columnCount) * zoom, 24.0, 512.0);
        return string.Equals(parameter as string, "GridWidth", StringComparison.Ordinal)
            ? cellSize * columnCount
            : cellSize;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture)
    {
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    private static double ReadDouble(object? value, double fallback)
    {
        return value is null || value == DependencyProperty.UnsetValue
            ? fallback
            : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}
