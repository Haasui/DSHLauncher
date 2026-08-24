using System.Windows.Media;

namespace DshLauncher.ViewModels;

/// <summary>共享状态色（与 Themes/Colors.xaml 保持一致，VM 侧直接使用）。</summary>
public static class StatusBrushes
{
    public static readonly SolidColorBrush Blue = Freeze(0x4C, 0x8D, 0xFF);
    public static readonly SolidColorBrush Green = Freeze(0x22, 0xC5, 0x5E);
    public static readonly SolidColorBrush Gray = Freeze(0x6B, 0x72, 0x80);
    public static readonly SolidColorBrush Red = Freeze(0xEF, 0x44, 0x44);
    public static readonly SolidColorBrush Orange = Freeze(0xF5, 0x9E, 0x0B);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
