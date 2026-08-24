using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DshLauncher.Converters;

/// <summary>null 或空 → Collapsed，否则 Visible（导航徽标用）。</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}