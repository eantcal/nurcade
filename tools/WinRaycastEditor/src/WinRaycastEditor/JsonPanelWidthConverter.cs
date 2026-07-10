using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinRaycastEditor;

/// <summary>
/// Converts the JSON panel visibility flag into a grid column width: a fixed pixel width
/// (from the converter parameter) when visible, otherwise zero so the column collapses.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) {
            return new GridLength(0);
        }

        if (parameter is string text
            && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var pixels)) {
            return new GridLength(pixels);
        }

        return GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
