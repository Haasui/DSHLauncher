using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DshLauncher.Converters;

/// <summary>bool→Visibility 反相：true→Collapsed，false→Visible（与 BooleanToVisibilityConverter 相反）。</summary>
public sealed class InvertBoolToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible ? false : true;
}
