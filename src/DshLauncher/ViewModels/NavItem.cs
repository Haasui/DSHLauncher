using CommunityToolkit.Mvvm.ComponentModel;

namespace DshLauncher.ViewModels;

/// <summary>左侧导航条目：文本 + 图标字形 + 关联页面 VM + 可选徽标。</summary>
public partial class NavItem : ObservableObject
{
    public NavItem(string label, string glyph, object page)
    {
        Label = label;
        Glyph = glyph;
        Page = page;
    }

    public string Label { get; }
    public string Glyph { get; }
    public object Page { get; }

    /// <summary>右上角小徽标（如更新提示）。</summary>
    [ObservableProperty]
    private string? _badge;
}