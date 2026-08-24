namespace DshLauncher.Core;

/// <summary>
/// 状态轮询器：每 2 秒用 IPGlobalProperties 探测一次端口是否可访问，
/// 变化时通过 <see cref="StatusChanged"/> 推送事件。
/// </summary>
public interface IStatusMonitor
{
    /// <summary>要探测的端口。</summary>
    int Port { get; set; }

    /// <summary>端口当前是否开放。</summary>
    bool IsPortOpen { get; }

    /// <summary>端口开放状态变化（参数为新的 IsPortOpen）。</summary>
    event EventHandler<bool>? StatusChanged;

    /// <summary>开始轮询（每 2s 一次）。</summary>
    void Start();

    /// <summary>停止轮询。</summary>
    void Stop();

    /// <summary>立即探测一次。</summary>
    Task<bool> CheckAsync(CancellationToken ct = default);
}
