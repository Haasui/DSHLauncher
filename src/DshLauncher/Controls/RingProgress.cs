using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DshLauncher.Core;

namespace DshLauncher.Controls;

/// <summary>
/// 环形进度（对齐 Napcat 环形图）：灰底圆环 + 蓝色进度弧 + 中心百分比文本。
/// 数值变化用 DoubleAnimation 平滑过渡（对齐 Napcat 的动态收缩，不再突跳）。
/// </summary>
public sealed class RingProgress : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(RingProgress),
        new FrameworkPropertyMetadata(0.0, OnPercentChanged));

    /// <summary>动画显示的当前值（Percent 变化时平滑过渡到此值）。</summary>
    internal static readonly DependencyProperty DisplayPercentProperty = DependencyProperty.Register(
        nameof(DisplayPercent), typeof(double), typeof(RingProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(RingProgress),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(RingProgress),
        new FrameworkPropertyMetadata(84.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public double DisplayPercent { get => (double)GetValue(DisplayPercentProperty); private set => SetValue(DisplayPercentProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public RingProgress()
    {
        // 主题切换（浅/深）时重绘，让环形颜色跟随
        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    /// <summary>从当前主题资源取颜色，取不到用回退色。</summary>
    private static Color ThemeColor(string key, Color fallback)
    {
        try { if (Application.Current.TryFindResource(key) is SolidColorBrush b) return b.Color; }
        catch { }
        return fallback;
    }

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var me = (RingProgress)d;
        // 从当前显示值平滑过渡到新目标（Napcat 动态收缩）
        var anim = new DoubleAnimation(me.DisplayPercent,
            Math.Clamp((double)e.NewValue, 0, 100),
            TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        me.BeginAnimation(DisplayPercentProperty, anim);
    }

    protected override Size MeasureOverride(Size availableSize)
        => new Size(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var d = Size;
        var thickness = Math.Max(5, d * 0.11);
        var center = new Point(d / 2, d / 2);
        var radius = (d - thickness) / 2;
        var pct = Math.Clamp(DisplayPercent, 0, 100) / 100.0;

        // 背景灰环
        var bg = new Pen(new SolidColorBrush(ThemeColor("Brush.Surface.Hover", Color.FromRgb(0xE2, 0xE8, 0xF0))), thickness);
        dc.DrawEllipse(null, bg, center, radius, radius);

        // 前景进度弧（蓝，从 12 点顺时针）
        if (pct > 0.001)
        {
            var angle = pct * 2 * Math.PI;
            var end = new Point(
                center.X + radius * Math.Sin(angle),
                center.Y - radius * Math.Cos(angle));
            var arc = new ArcSegment(end, new Size(radius, radius), 0,
                pct > 0.5, SweepDirection.Clockwise, true);
            var fig = new PathFigure { StartPoint = new Point(center.X, center.Y - radius), IsClosed = false };
            fig.Segments.Add(arc);
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            var fg = new Pen(new SolidColorBrush(ThemeColor("Brush.Accent.Blue", Color.FromRgb(0x4C, 0x8D, 0xFF))), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, fg, geo);
        }

        // 中心文字：百分比（跟动画值走）
        var text = $"{Math.Round(DisplayPercent)}%";
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), d * 0.22, new SolidColorBrush(ThemeColor("Brush.Text.Primary", Color.FromRgb(0x1A, 0x22, 0x33))),
            1.25);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));

        // 标签（百分比下方小字）
        if (!string.IsNullOrEmpty(Label))
        {
            var lf = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), d * 0.12, new SolidColorBrush(ThemeColor("Brush.Text.Secondary", Color.FromRgb(0x5A, 0x64, 0x78))),
                1.25);
            dc.DrawText(lf, new Point(center.X - lf.Width / 2, center.Y - ft.Height / 2 + d * 0.2));
        }
    }
}
