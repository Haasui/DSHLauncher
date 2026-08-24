namespace DshLauncher.Core;

/// <summary>日志来源。</summary>
public enum LogSource
{
    Stdout,
    Stderr,
}

/// <summary>一行日志（时间戳 + 来源 + 文本）。</summary>
public sealed record LogLine(DateTime Timestamp, LogSource Source, string Text);
