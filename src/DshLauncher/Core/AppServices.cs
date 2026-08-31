namespace DshLauncher.Core;

/// <summary>
/// 简易组合根：创建并关联各 Core 服务（单用户桌面应用，无需 DI 容器）。
/// 后续阶段在此挂 Skin/Plugin。
/// </summary>
public static class AppServices
{
    public static IDshService Dsh { get; } = new DshService();
    public static IStatusMonitor Status { get; } = new StatusMonitor();
    public static ILogService Log { get; } = new LogService();
    public static ISettingsService Settings { get; } = new SettingsService();
    public static IDoctorService Doctor { get; } = new DoctorService();
    public static IUpdateService Update { get; } = new UpdateService();

    public static IPluginService Plugin { get; } = new PluginService();
    public static PluginStoreService Store { get; } = new PluginStoreService();
    public static ApprovalMonitor Approval { get; } = new ApprovalMonitor();

    static AppServices()
    {
        // DSH 进程输出 → 内存日志缓冲（日志页）+ 持久化 dsh.log（启动失败时排查）
        Dsh.StdoutReceived += (_, line) =>
        {
            Log.Append(line, LogSource.Stdout);
            FileLog.AppendDsh(line, LogSource.Stdout);
        };
        Dsh.StderrReceived += (_, line) =>
        {
            Log.Append(line, LogSource.Stderr);
            FileLog.AppendDsh(line, LogSource.Stderr);
        };
        Dsh.Exited += (_, _) =>
        {
            Log.Append("[启动器] DSH 进程已退出。", LogSource.Stderr);
            FileLog.MarkDsh("DSH 进程已退出");
        };
    }
}
