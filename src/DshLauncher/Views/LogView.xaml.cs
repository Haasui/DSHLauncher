using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using DshLauncher.ViewModels;

namespace DshLauncher.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _attachedVm;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 解绑旧 VM（避免内存泄漏：VM 持有 View 引用 → View 持有 VM 引用）
        if (_attachedVm is not null) _attachedVm.Lines.CollectionChanged -= OnLinesChanged;
        _attachedVm = e.NewValue as LogViewModel;
        if (_attachedVm is not null) _attachedVm.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_attachedVm is not null)
        {
            _attachedVm.Lines.CollectionChanged -= OnLinesChanged;
            _attachedVm = null;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_attachedVm is null || !_attachedVm.AutoScroll) return;
        if (LogList.Items.Count == 0) return;
        // 滚到最后一项（不抛滚动事件，避免双向反馈卡 UI）
        var last = LogList.Items[LogList.Items.Count - 1];
        LogList.ScrollIntoView(last);
    }
}
