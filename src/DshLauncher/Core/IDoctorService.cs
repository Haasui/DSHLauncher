namespace DshLauncher.Core;

/// <summary>单项检测结果。</summary>
public sealed record DoctorCheck(string Name, CheckStatus Status, string Detail);

/// <summary>检测结果状态。</summary>
public enum CheckStatus
{
    Pass,
    Warn,
    Fail,
    Info,
}

/// <summary>
/// 诊断服务：一键检测 Node / npm / dsh / 端口 / 磁盘 / 内存 / 网络（诊断页）。
/// </summary>
public interface IDoctorService
{
    /// <summary>运行全部检测项，返回结果列表。</summary>
    Task<IReadOnlyList<DoctorCheck>> RunAllAsync(CancellationToken ct = default);
}
