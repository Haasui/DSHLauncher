using System.Globalization;
using System.Windows.Data;

namespace DshLauncher.Converters;

/// <summary>布尔取反（用于「已隔离时禁用更新按钮」等）。</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
