namespace DshLauncher.Core;

/// <summary>版本对比结果（local vs npm latest）。</summary>
public sealed record VersionInfo(string? Local, string? Latest, bool UpdateAvailable);

/// <summary>三源版本（当前 / npm latest / npm @next，best-effort）。</summary>
public sealed record DshVersionSources(string? Current, string? Latest, string? Next);

/// <summary>
/// 更新服务：本地 dsh 版本 vs npm latest/@next，一键更新（npm install -g）。
/// </summary>
public interface IUpdateService
{
    /// <summary>获取本地与 npm latest 版本（现状接口）。</summary>
    Task<VersionInfo> GetVersionsAsync(CancellationToken ct = default);

    /// <summary>获取三源版本。</summary>
    Task<DshVersionSources> GetSourcesAsync(CancellationToken ct = default);

    /// <summary>一键更新到指定渠道（latest|next）。</summary>
    Task<bool> UpdateToAsync(string channel, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>一键更新（npm install -g @latest）。</summary>
    Task<bool> UpdateAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}