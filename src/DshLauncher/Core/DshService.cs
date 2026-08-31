using System.Diagnostics;
using System.Text;

namespace DshLauncher.Core;

/// <summary>
/// 启动器核心：spawn 一条 <c>npx @deepseek-ai/dsh web</c>（唯一使用路径），
/// 接管 stdout/stderr；停止 = taskkill /F /T /PID 杀整个进程树。
/// 绝不误杀其他进程：只操作自己 spawn 的进程树。
/// </summary>
public sealed class DshService : IDshService
{
    private const int StopTimeoutMs = 10_000;
    private static readonly char[] ForbiddenArgChars = { '"', '&', '|', '<', '>', '^', '%', '\r', '\n' };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processLock = new();
    private Process? _process;
    private bool _stopRequested;

    public bool IsRunning
    {
        get
        {
            lock (_processLock)
            {
                var p = _process;
                return p is { HasExited: false };
            }
        }
    }

    public int? Pid
    {
        get { lock (_processLock) return _process?.Id; }
    }

    public DateTime? StartedAt { get; private set; }

    /// <summary>完整 spawn 命令行（含 extraArgs/patchFile，可能含敏感参数）。仅供本地调试日志，不得显示到 UI。</summary>
    public string CommandLine { get; private set; } = string.Empty;

    /// <summary>脱敏命令行概要（仅端口，不含任何用户参数），供 Home/状态栏等用户可见 UI 展示。</summary>
    public string SafeCommandLine { get; private set; } = string.Empty;

    public bool WasStopRequested => _stopRequested;

    public event EventHandler<string>? StdoutReceived;
    public event EventHandler<string>? StderrReceived;
    public event EventHandler<int>? Exited;

    public async Task<bool> StartAsync(int port, string? extraArgs = null, string? patchFile = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsRunning) return true;

            if (PortProbe.IsListening(port))
                throw new InvalidOperationException(
                    $"端口 {port} 已被其他程序占用，请先在设置页更换端口（启动器不会自动杀掉占用者）。");

            var commandText = BuildCommandText(port, extraArgs, patchFile);
            CommandLine = commandText;
            SafeCommandLine = $"npx @deepseek-ai/dsh web --no-open --port {port}"; // 用户可见：只含端口，不含 extraArgs/patchFile

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(commandText);

            _stopRequested = false;
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 cmd.exe");
            lock (_processLock) { _process = proc; }
            StartedAt = DateTime.Now;
            FileLog.MarkDsh($"spawn PID {proc.Id} | {SafeCommandLine}");

            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) StdoutReceived?.Invoke(this, e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) StderrReceived?.Invoke(this, e.Data);
            };
            proc.Exited += (_, _) => OnProcessExited();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Process? p;
            lock (_processLock) { p = _process; }
            if (p is null || p.HasExited)
            {
                lock (_processLock) { _process = null; }
                return true;
            }

            _stopRequested = true;
            int pid;
            try { pid = p.Id; }
            catch { return true; } // 进程刚被外部杀，已 disposed

            try
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {pid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (killer is not null) await killer.WaitForExitAsync(ct);
            }
            catch
            {
                // taskkill 失败时进程可能仍在退出；交给 WaitForExitAsync 兜底
            }

            try
            {
                await Task.WhenAny(p.WaitForExitAsync(ct), Task.Delay(StopTimeoutMs, ct));
            }
            catch
            {
                // 忽略取消/超时
            }

            try
            {
                if (!p.HasExited)
                {
                    // 强制杀树失败：不扩大打击面（例如尝试杀全部 node），仅报告
                    return false;
                }
            }
            catch
            {
                // 进程在 HasExited 访问时已 disposed（Exited 事件并发清掉了 _process）
            }

            lock (_processLock) { _process = null; }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RestartAsync(int port, string? extraArgs = null, string? patchFile = null, CancellationToken ct = default)
    {
        await StopAsync(ct);
        return await StartAsync(port, extraArgs, patchFile, ct);
    }

    /// <summary>停止外部启动的 DSH：netstat 找端口监听 PID，taskkill /F /T 杀整树。仅确认后调用。</summary>
    public async Task<bool> StopExternalAsync(int port, CancellationToken ct = default)
    {
        var r = await CommandRunner.RunAsync($"netstat -ano | findstr :{port}", TimeSpan.FromSeconds(10), ct);
        foreach (var raw in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!raw.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
            var tokens = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 5 && int.TryParse(tokens[^1], out var pid) && pid > 0)
            {
                var kill = await CommandRunner.RunAsync($"taskkill /F /T /PID {pid}", TimeSpan.FromSeconds(10), ct);
                return kill.ExitCode == 0;
            }
        }
        return false; // 没找到监听者
    }

    private void OnProcessExited()
    {
        Process? p;
        int code = -1;
        lock (_processLock)
        {
            p = _process;
            _process = null;
        }
        try
        {
            code = p?.ExitCode ?? -1;
        }
        catch
        {
            // 退出码不可得
        }
        Exited?.Invoke(this, code);
    }

    private static string BuildCommandText(int port, string? extraArgs, string? patchFile)
    {
        if (!string.IsNullOrWhiteSpace(extraArgs) && extraArgs.IndexOfAny(ForbiddenArgChars) >= 0)
            throw new InvalidOperationException("启动参数包含不允许的字符（引号/重定向符等）。");

        if (!string.IsNullOrWhiteSpace(patchFile))
        {
            var pf = patchFile.Trim();
            if (pf.IndexOfAny(ForbiddenArgChars) >= 0 || pf.Contains(' '))
                throw new InvalidOperationException("补丁文件路径不能包含空格/引号/重定向符等特殊字符（--patch）。");
        }

        var sb = new StringBuilder("npx @deepseek-ai/dsh web --no-open --port ");
        sb.Append(port);
        if (!string.IsNullOrWhiteSpace(patchFile)) sb.Append(" --patch ").Append(patchFile.Trim());
        if (!string.IsNullOrWhiteSpace(extraArgs)) sb.Append(' ').Append(extraArgs.Trim());
        return sb.ToString();
    }
}
