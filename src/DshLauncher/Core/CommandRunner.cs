using System.Diagnostics;
using System.Text;

namespace DshLauncher.Core;

/// <summary>命令执行结果。</summary>
public sealed record CommandResult(int ExitCode, string Output);

/// <summary>运行一条命令（cmd /c），捕获输出，带超时。只操作自己 spawn 的进程树，超时才 Kill。</summary>
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string command, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("/d");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(command);

        using var p = Process.Start(psi);
        if (p is null) return new CommandResult(-1, "无法启动进程");

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        var wait = p.WaitForExitAsync(ct);
        var done = await Task.WhenAny(wait, Task.Delay(timeout, ct));
        var timedOut = done != wait;
        if (timedOut)
        {
            try { p.Kill(true); } catch { }
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        int code;
        try { code = p.ExitCode; } catch { code = -1; }
        if (timedOut) return new CommandResult(-1, $"执行超时（> {timeout.TotalSeconds:0}s）");
        return new CommandResult(code, (stdout + stderr).Trim());
    }
}
