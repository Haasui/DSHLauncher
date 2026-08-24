using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>日志页 VM：订阅 LogService，批量追加到 UI 集合；支持暂停/恢复/清空/自动滚动。</summary>
public partial class LogViewModel : ObservableObject
{
    private readonly ILogService _log;
    private readonly Dispatcher _ui;

    public ObservableCollection<LogLine> Lines { get; } = new();

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _autoScroll = true;

    public string PauseButtonText => IsPaused ? "继续" : "暂停";

    public int LineCount => Lines.Count;

    public LogViewModel(ILogService log)
    {
        _log = log;
        _ui = Dispatcher.CurrentDispatcher;
        _log.LinesAppended += OnLinesAppended;
        _log.Cleared += (_, _) => _ui.InvokeAsync(() =>
        {
            Lines.Clear();
            OnPropertyChanged(nameof(LineCount));
        });
        foreach (var line in _log.Snapshot()) Lines.Add(line);
        OnPropertyChanged(nameof(LineCount));
    }

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(PauseButtonText));

    private void OnLinesAppended(object? sender, IReadOnlyList<LogLine> batch)
    {
        _ui.InvokeAsync(() =>
        {
            if (IsPaused) return;
            foreach (var line in batch) Lines.Add(line);
            OnPropertyChanged(nameof(LineCount));
        });
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
        {
            IsPaused = false;
            _log.Resume(); // 补发暂停期间增量 → LinesAppended → 追加
        }
        else
        {
            IsPaused = true;
            _log.Pause();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _log.Clear();
        Lines.Clear();
        OnPropertyChanged(nameof(LineCount));
    }

    [RelayCommand]
    private void CopyAll()
    {
        try
        {
            var text = string.Join(Environment.NewLine,
                Lines.Select(l => $"[{l.Timestamp:HH:mm:ss.fff}] {l.Source} {l.Text}"));
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板不可用时忽略
        }
    }
}
