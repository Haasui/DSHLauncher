using System.IO;

namespace DshLauncher.Core;

/// <summary>启动器自身调试日志（%APPDATA%\DshLauncher\launcher.log），失败静默。</summary>
public static class FileLog
{
    private static readonly object Gate = new();

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
}
