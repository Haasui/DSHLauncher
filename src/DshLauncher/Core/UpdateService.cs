namespace DshLauncher.Core;

/// <summary>更新服务：本地 vs npm latest/@next，一键 npm install -g。</summary>
public sealed class UpdateService : IUpdateService
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);

    public async Task<VersionInfo> GetVersionsAsync(CancellationToken ct = default)
    {
        var local = await GetLocalVersionAsync(ct);
        var latest = await GetLatestVersionAsync(ct);
        var localSem = Semver.TryParse(local);
        var latestSem = Semver.TryParse(latest);
        var available = latestSem is not null && (localSem is null || latestSem.CompareTo(localSem) > 0);
        return new VersionInfo(local, latest, available);
    }

    public async Task<DshVersionSources> GetSourcesAsync(CancellationToken ct = default)
    {
        var current = await GetLocalVersionAsync(ct);
        var latest = await GetLatestVersionAsync(ct);
        var next = await GetNextVersionAsync(ct);
        return new DshVersionSources(current, latest, next);
    }

    private static async Task<string?> GetLocalVersionAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("npx @deepseek-ai/dsh --version", CheckTimeout, ct);
        if (r.ExitCode != 0) return null;
        return Semver.ExtractVersion(r.Output);
    }

    private static async Task<string?> GetLatestVersionAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("npm view @deepseek-ai/dsh version", CheckTimeout, ct);
        return r.ExitCode == 0 ? Semver.ExtractVersion(r.Output) : null;
    }

    private static async Task<string?> GetNextVersionAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("npm view @deepseek-ai/dsh@next version", CheckTimeout, ct);
        return r.ExitCode == 0 ? Semver.ExtractVersion(r.Output) : null;
    }

    public async Task<bool> UpdateAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => await UpdateToAsync("latest", progress, ct);

    public async Task<bool> UpdateToAsync(string channel, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var spec = channel == "next" ? "@deepseek-ai/dsh@next" : "@deepseek-ai/dsh@latest";
        var reg = AppServices.Settings.NpmRegistry;
        var regFlag = string.IsNullOrWhiteSpace(reg) ? "" : $" --registry \"{reg.Trim()}\"";
        progress?.Report($"正在执行 npm install -g {spec}{regFlag} …");
        var r = await CommandRunner.RunAsync($"npm install -g {spec}{regFlag}", UpdateTimeout, ct);
        if (r.ExitCode != 0)
        {
            progress?.Report("更新失败：" + (string.IsNullOrEmpty(r.Output) ? "未知错误" : r.Output));
            return false;
        }
        progress?.Report("更新完成：" + (string.IsNullOrEmpty(r.Output) ? "npm install 已成功" : r.Output));
        return true;
    }
}
