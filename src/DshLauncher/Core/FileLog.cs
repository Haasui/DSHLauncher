using System.IO;

namespace DshLauncher.Core;

/// <summary>
/// 启动器自身调试日志（%APPDATA%\DshLauncher\launcher.log），失败静默。
/// 另加一条独立的 DSH 进程输出流（%APPDATA%\DshLauncher\dsh.log），
/// 让「拉起超时」等启动问题可以从磁盘直接读到 DSH 到底卡在哪一步。
/// </summary>
public static class FileLog
{
    private static readonly object Gate = new();

    /// <summary>DSH 进程 stdout/stderr 持久化日志路径。</summary>
    public static string DshLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshLauncher", "dsh.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshLauncher");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "launcher.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch
        {
        }
    }

    /// <summary>追加一行 DSH 进程输出到 dsh.log（来源区分 out/err）。线程安全，失败静默。</summary>
    public static void AppendDsh(string line, LogSource source)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(DshLogPath)!;
                Directory.CreateDirectory(dir);
                File.AppendAllText(DshLogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] [{(source == LogSource.Stderr ? "err" : "out")}] {line}\n");
            }
        }
        catch
        {
        }
    }

    /// <summary>在 dsh.log 写入一个分隔标记（划分每次启动/停止，便于读日志尾部定位本次启动）。</summary>
    public static void MarkDsh(string text)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(DshLogPath)!;
                Directory.CreateDirectory(dir);
                File.AppendAllText(DshLogPath, $"\n[{DateTime.Now:HH:mm:ss.fff}] === {text} ===\n");
            }
        }
        catch
        {
        }
    }

    /// <summary>读取 dsh.log 尾部至多 maxLines 行（不足则全量）；文件不存在/读失败返回空数组。</summary>
    public static IReadOnlyList<string> ReadDshTail(int maxLines = 30)
    {
        try
        {
            if (!File.Exists(DshLogPath)) return Array.Empty<string>();
            var lines = File.ReadAllLines(DshLogPath);
            if (lines.Length <= maxLines) return lines;
            return lines[^maxLines..];
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
