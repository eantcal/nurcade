using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinRaycastEditor;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}

public sealed class SpriteMapMarkerSizeConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var cellSize = ReadDouble(values.ElementAtOrDefault(0), 48.0);
        var scaleCells = ReadDouble(values.ElementAtOrDefault(1), 1.0);
        if (!double.IsFinite(cellSize) || cellSize <= 0.0) {
            return 6.0;
        }

        if (!double.IsFinite(scaleCells) || scaleCells <= 0.0) {
            scaleCells = 1.0;
        }

        return Math.Clamp(
            cellSize * 0.68 * scaleCells,
            6.0,
            Math.Max(6.0, cellSize * 2.5));
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

public sealed class SpriteMapMarkerOffsetConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var size = ReadDouble(values.ElementAtOrDefault(0), 48.0);
        var offset = ReadDouble(values.ElementAtOrDefault(1), 0.0);
        return size * offset;
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
