using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DshLauncher.Core;

/// <summary>启动器自更新检查失败时抛出，携带用户可感知的失败原因。</summary>
public sealed class LauncherUpdateException : Exception
{
    /// <summary>GitHub 是否有 HTTP 响应；true 表示仓库存在但未发布 release，false 表示网络层连不上。</summary>
    public bool RepoResponded { get; }
    public LauncherUpdateException(string message, bool repoResponded) : base(message) => RepoResponded = repoResponded;
}

/// <summary>
/// 启动器自更新：检查启动器自己的 GitHub release，下载新 exe 并在退出后替换自身。
/// 仓库地址是编译期常量 <see cref="ReleaseRepoUrl"/>；仓库尚未发布 release 时优雅降级展示状态。
/// </summary>
public static class LauncherUpdater
{
    /// <summary>launcher 仓库自更新地址（GitHub releases API）。这是编译期常量，发布时改成真实仓库。</summary>
    public const string ReleaseRepoUrl = "https://api.github.com/repos/Haasui/DSHLauncher/releases/latest";

    /// <summary>启动器项目主页（GitHub 仓库），「关于」页链接使用。</summary>
    public const string RepositoryUrl = "https://github.com/Haasui/DSHLauncher";

    /// <summary>当前 launcher 版本（取 AssemblyInformationalVersion，含 +hash 则去掉）。</summary>
    public static string CurrentVersion()
    {
        var iv = typeof(LauncherUpdater).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(iv))
        {
            var plus = iv.IndexOf('+');
            if (plus >= 0) iv = iv[..plus];
            return iv;
        }
        return typeof(LauncherUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public sealed record LauncherRelease(string? Tag, string? DownloadUrl, string? Body, bool UpdateAvailable);

    /// <summary>检查 GitHub latest。releases/latest 返回单个版本对象；失败抛 <see cref="LauncherUpdateException"/>。</summary>
    public static async Task<LauncherRelease> CheckAsync(CancellationToken ct = default)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        hc.DefaultRequestHeaders.UserAgent.ParseAdd("DshLauncher/" + CurrentVersion());
        try
        {
            var json = await hc.GetStringAsync(ReleaseRepoUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = Str(root, "tag_name");
            var body = Str(root, "body");
            var url = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name");
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = Str(a, "browser_download_url");
                        break;
                    }
                }
            }
            var current = Semver.TryParse(CurrentVersion());
            var latest = Semver.TryParse(tag);
            var available = latest is not null && (current is null || latest.CompareTo(current) > 0);
            return new LauncherRelease(tag, url, body, available);
        }
        catch (HttpRequestException ex)
        {
            // GitHub 有响应（如 releases/latest 404：仓库存在但未发布 release，或仓库不存在）→ 视为「暂无发布版本」
            if (ex.StatusCode is not null)
                throw new LauncherUpdateException("GitHub 上暂无该仓库的发布版本。", repoResponded: true);
            // 网络层失败（DNS/超时/连接被重置）→「不可达」
            throw new LauncherUpdateException("无法连接 GitHub，请检查网络。", repoResponded: false);
        }
        catch
        {
            throw new LauncherUpdateException("无法连接 GitHub，请检查网络。", repoResponded: false);
        }
    }

    /// <summary>下载新版本 exe 到更新目录。</summary>
    public static async Task<string> DownloadAsync(string url, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DshLauncher", "updates");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "DshLauncher.new.exe");
        using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var bytes = await hc.GetByteArrayAsync(url, ct);
        await File.WriteAllBytesAsync(dest, bytes, ct);
        progress?.Report($"{bytes.Length / 1024} KB");
        return dest;
    }

    /// <summary>生成替换脚本并启动：等待当前 exe 退出后覆盖，再重启（薄壳自更新）。</summary>
    public static void ScheduleReplace(string newExePath)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DshLauncher", "updates");
        Directory.CreateDirectory(dir);
        var bat = Path.Combine(dir, "selfupdate.cmd");
        var script =
$"""
@echo off
set "NEW={newExePath}"
set "CUR={current}"
:retry
copy /y "%NEW%" "%CUR%" >nul 2>nul
if not errorlevel 1 goto done
timeout /t 1 /nobreak >nul
goto retry
:done
start "" "%CUR%"
del "%~f0" >nul 2>nul
exit
""";
        File.WriteAllText(bat, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static string Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
}
