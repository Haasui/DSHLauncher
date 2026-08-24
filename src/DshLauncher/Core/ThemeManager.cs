using System.Windows;

namespace DshLauncher.Core;

/// <summary>
/// 主题运行时管理（实时热切）：把 MergedDictionaries[0]（Colors 主题字典）整体替换，
/// 所有引用 DynamicResource 的控件会立即刷新，无需重启。
/// </summary>
public static class ThemeManager
{
    /// <summary>主题切换后触发（供自绘控件如 RingProgress 重绘）。</summary>
    public static event Action? ThemeChanged;

    /// <summary>应用主题（light | dark）——替换主题字典，控件即时刷新。</summary>
    public static void Apply(string theme)
    {
        try
        {
            if (Application.Current?.Resources?.MergedDictionaries is not { Count: > 0 } dicts) return;
            var dark = theme == "dark";
            var uri = new Uri(dark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.xaml", UriKind.Relative);
            dicts[0] = new ResourceDictionary { Source = uri };
        }
        catch
        {
            // 主题切换失败不崩溃
        }
        ThemeChanged?.Invoke();
    }

    /// <summary>解析最终深浅色：light | dark。</summary>
    public static string Resolve(string theme) => theme == "dark" ? "dark" : "light";
}
