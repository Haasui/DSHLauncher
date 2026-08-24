namespace DshLauncher.Core;

/// <summary>
/// 日志缓冲：数组 + 截断（10 万行不卡），150ms 批量刷新；
/// 暂停只停推送、缓冲继续，Resume 补发暂停期间增量。
/// </summary>
public sealed class LogService : ILogService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);
    private const int MaxLinesDefault = 100_000;

    private readonly object _gate = new();
    private readonly List<LogLine> _buffer = new();
    private bool _flushScheduled;
    private int _flushedUpTo; // 已推送到的缓冲下标
    private bool _isPaused;
    private int _maxLines = MaxLinesDefault;

    public int MaxLines
    {
        get { lock (_gate) return _maxLines; }
        set { lock (_gate) { _maxLines = value; TrimLocked(); } }
    }

    public bool IsPaused
    {
        get { lock (_gate) return _isPaused; }
    }

    public event EventHandler<IReadOnlyList<LogLine>>? LinesAppended;
    public event EventHandler? Cleared;

    public void Append(string text, LogSource source)
    {
        lock (_gate)
        {
            _buffer.Add(new LogLine(DateTime.Now, source, text));
            TrimLocked();
            if (_isPaused) return;
            if (!_flushScheduled)
            {
                _flushScheduled = true;
                _ = FlushAsync();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
            _flushedUpTo = 0;
            _flushScheduled = false;
        }
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        lock (_gate) _isPaused = true;
    }

    public void Resume()
    {
        IReadOnlyList<LogLine>? delta = null;
        lock (_gate)
        {
            _isPaused = false;
            if (_flushedUpTo < _buffer.Count)
            {
                delta = _buffer.GetRange(_flushedUpTo, _buffer.Count - _flushedUpTo);
                _flushedUpTo = _buffer.Count;
            }
        }
        if (delta is { Count: > 0 }) LinesAppended?.Invoke(this, delta);
    }

    public IReadOnlyList<LogLine> Snapshot()
    {
        lock (_gate) return _buffer.ToArray();
    }

    private async Task FlushAsync()
    {
        await Task.Delay(FlushInterval);
        IReadOnlyList<LogLine>? batch = null;
        lock (_gate)
        {
            _flushScheduled = false;
            if (_isPaused) return;
            if (_flushedUpTo >= _buffer.Count) return;
            batch = _buffer.GetRange(_flushedUpTo, _buffer.Count - _flushedUpTo);
            _flushedUpTo = _buffer.Count;
        }
        if (batch is { Count: > 0 }) LinesAppended?.Invoke(this, batch);
    }

    private void TrimLocked()
    {
        var over = _buffer.Count - _maxLines;
        if (over <= 0) return;
        _buffer.RemoveRange(0, over);
        _flushedUpTo = Math.Max(0, _flushedUpTo - over);
    }
}
