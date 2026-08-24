namespace DshLauncher.Core;

/// <summary>
/// 日志服务：接收 DSH 进程的 stdout/stderr，用数组 + 截断缓冲（不是字符串拼接），
/// 支持暂停/恢复/清空，实时向 UI 推送。目标：10 万行不卡。
/// </summary>
public interface ILogService
{
    /// <summary>缓冲上限（默认 100_000）。</summary>
    int MaxLines { get; set; }

    /// <summary>是否已暂停（暂停期间继续收集但不推送 UI）。</summary>
    bool IsPaused { get; }

    /// <summary>追加一行（带时间戳/来源）。</summary>
    void Append(string text, LogSource source);

    /// <summary>新行到达时触发（携带增量行）。</summary>
    event EventHandler<IReadOnlyList<LogLine>>? LinesAppended;

    /// <summary>缓冲被清空时触发。</summary>
    event EventHandler? Cleared;

    /// <summary>清空缓冲。</summary>
    void Clear();

    /// <summary>暂停推送（缓冲仍继续，增量在 Resume 时一并补发）。</summary>
    void Pause();

    /// <summary>恢复推送并补发暂停期间的增量。</summary>
    void Resume();

    /// <summary>当前缓冲快照。</summary>
    IReadOnlyList<LogLine> Snapshot();
}
