namespace DshLauncher.Core;

/// <summary>
/// 启动器核心服务：负责 spawn 一条 <c>npx @deepseek-ai/dsh web</c> 命令，
/// 接管其 stdout/stderr，并管理其生命周期（启动/停止/重启）。
/// 绝不做 adapter/协议探测/版本匹配（已否决过度设计）。
/// </summary>
/// <remarks>停止只针对自己 spawn 的进程树（taskkill /F /T /PID），绝不误杀其他进程。</remarks>
public interface IDshService
{
    /// <summary>进程是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>真实 PID（spawn 后获取）。</summary>
    int? Pid { get; }

    /// <summary>启动时间。</summary>
    DateTime? StartedAt { get; }

    /// <summary>spawn 的命令行（可能含敏感参数，仅供本地日志，不得显示到 UI）。</summary>
    string CommandLine { get; }

    /// <summary>脱敏命令行概要（仅端口），供用户可见 UI 展示。</summary>
    string SafeCommandLine { get; }

    /// <summary>本次退出是否为主动停止（StopAsync 触发）。</summary>
    bool WasStopRequested { get; }

    /// <summary>标准输出一行。</summary>
    event EventHandler<string>? StdoutReceived;

    /// <summary>标准错误一行。</summary>
    event EventHandler<string>? StderrReceived;

    /// <summary>进程退出（含退出码）。</summary>
    event EventHandler<int>? Exited;

    /// <summary>启动 DSH。返回是否成功；端口被占时抛 InvalidOperationException。patchFile 为官方 --patch 覆盖层。</summary>
    Task<bool> StartAsync(int port, string? extraArgs = null, string? patchFile = null, CancellationToken ct = default);

    /// <summary>停止：taskkill /F /T /PID 杀整个进程树。返回是否已完全停止。</summary>
    Task<bool> StopAsync(CancellationToken ct = default);

    /// <summary>停止后重新启动。</summary>
    Task<bool> RestartAsync(int port, string? extraArgs = null, string? patchFile = null, CancellationToken ct = default);

    /// <summary>
    /// 停止「外部启动的 DSH」（接管模式）：通过 netstat 找端口监听 PID 再 taskkill /F /T 杀整树。
    /// 仅由用户显式确认后调用（HomeViewModel 弹确认框）；调用前需 host.describe 验证端口确为 DSH。
    /// </summary>
    Task<bool> StopExternalAsync(int port, CancellationToken ct = default);
}
