using System.IO;
using System.Net.Http;

namespace DshLauncher.Core;

/// <summary>一键诊断（诊断页）：Node/npm/dsh/端口/磁盘/内存/网络/WebView2。</summary>
public sealed class DoctorService : IDoctorService
{
    private static readonly TimeSpan CmdTimeout = TimeSpan.FromSeconds(25);

    private static string NpmBase() => string.IsNullOrWhiteSpace(AppServices.Settings.NpmRegistry) ? "https://registry.npmjs.org" : AppServices.Settings.NpmRegistry!;

    public async Task<IReadOnlyList<DoctorCheck>> RunAllAsync(CancellationToken ct = default)
    {
        return new List<DoctorCheck>
        {
            await CheckNodeAsync(ct),
            await CheckNpmAsync(ct),
            await CheckDshAsync(ct),
            CheckPort(AppServices.Settings.Port),
            CheckDisk(),
            CheckMemory(),
            await CheckNetworkAsync(ct),
            await CheckNpmLatencyAsync(ct),
            CheckDshHomeSize(),
            CheckProfile(),
            CheckWebView2(),
        };
    }

    private static async Task<DoctorCheck> CheckNpmLatencyAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await client.GetAsync(NpmBase(), cts.Token);
            sw.Stop();
            var ms = sw.ElapsedMilliseconds;
            return new DoctorCheck("npm 延迟", resp.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warn,
                $"{ms} ms（HTTP {(int)resp.StatusCode}）");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("npm 延迟", CheckStatus.Fail, ex.Message);
        }
    }

    private static DoctorCheck CheckDshHomeSize()
    {
        try
        {
            if (!Directory.Exists(DshPaths.Home)) return new DoctorCheck("~/.dsh 大小", CheckStatus.Info, "目录不存在");
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(DshPaths.Home, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(f).Length; } catch { }
            }
            var gb = bytes / (1024.0 * 1024 * 1024);
            return gb >= 1
                ? new DoctorCheck("~/.dsh 大小", CheckStatus.Info, $"{gb:0.00} GB")
                : new DoctorCheck("~/.dsh 大小", CheckStatus.Info, $"{bytes / 1024.0 / 1024:0.0} MB");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("~/.dsh 大小", CheckStatus.Warn, ex.Message);
        }
    }

    private static DoctorCheck CheckProfile()
    {
        try
        {
            if (!File.Exists(DshPaths.WebPackageJson)) return new DoctorCheck("web profile", CheckStatus.Warn, "profiles/web/package.json 缺失");
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(DshPaths.WebPackageJson));
            if (!doc.RootElement.TryGetProperty("dsh", out var dsh)
                || !dsh.TryGetProperty("profile", out var profile)
                || !profile.TryGetProperty("bundles", out var bundles))
                return new DoctorCheck("web profile", CheckStatus.Warn, "package.json 缺少 dsh.profile.bundles");
            var count = bundles.GetArrayLength();
            return new DoctorCheck("web profile", CheckStatus.Pass, $"可解析，bundles={count}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("web profile", CheckStatus.Warn, ex.Message);
        }
    }

    private static async Task<DoctorCheck> CheckNodeAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("node --version", CmdTimeout, ct);
        return Semver.TryParse(r.Output) is { } v
            ? new DoctorCheck("Node.js", CheckStatus.Pass, $"已安装 v{v}")
            : new DoctorCheck("Node.js", CheckStatus.Fail, string.IsNullOrEmpty(r.Output) ? "未找到 node" : r.Output);
    }

    private static async Task<DoctorCheck> CheckNpmAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("npm --version", CmdTimeout, ct);
        return r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.Output)
            ? new DoctorCheck("npm", CheckStatus.Pass, $"已安装 v{r.Output.Split('\n')[0]}")
            : new DoctorCheck("npm", CheckStatus.Fail, string.IsNullOrEmpty(r.Output) ? "未找到 npm" : r.Output);
    }

    private static async Task<DoctorCheck> CheckDshAsync(CancellationToken ct)
    {
        var r = await CommandRunner.RunAsync("npx @deepseek-ai/dsh --version", CmdTimeout, ct);
        var version = Semver.ExtractVersion(r.Output);
        return version is not null
            ? new DoctorCheck("dsh", CheckStatus.Pass, $"已安装 {version}")
            : new DoctorCheck("dsh", CheckStatus.Fail, string.IsNullOrEmpty(r.Output) ? "未找到 dsh" : r.Output);
    }

    private static DoctorCheck CheckPort(int port)
        => PortProbe.IsListening(port)
            ? new DoctorCheck($"端口 {port}", CheckStatus.Info, "正在被监听（可能是 DSH 或其他程序）")
            : new DoctorCheck($"端口 {port}", CheckStatus.Info, "空闲");

    private static DoctorCheck CheckDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\");
            if (drive.IsReady)
            {
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                return freeGb >= 1
                    ? new DoctorCheck("磁盘", CheckStatus.Pass, $"剩余 {freeGb:0.0} GB")
                    : new DoctorCheck("磁盘", CheckStatus.Warn, $"剩余仅 {freeGb:0.0} GB");
            }
            return new DoctorCheck("磁盘", CheckStatus.Warn, "驱动器未就绪");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("磁盘", CheckStatus.Warn, ex.Message);
        }
    }

    private static DoctorCheck CheckMemory()
    {
        try
        {
            var mb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024);
            return mb >= 2048
                ? new DoctorCheck("内存", CheckStatus.Pass, $"可用约 {mb:0} MB")
                : new DoctorCheck("内存", CheckStatus.Warn, $"可用仅 {mb:0} MB");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("内存", CheckStatus.Warn, ex.Message);
        }
    }

    private static async Task<DoctorCheck> CheckNetworkAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var resp = await client.GetAsync(NpmBase(), cts.Token);
            return new DoctorCheck("网络", resp.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warn,
                $"npm registry {(resp.IsSuccessStatusCode ? "可达" : $"HTTP {(int)resp.StatusCode}")}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("网络", CheckStatus.Fail, ex.Message);
        }
    }

    private static DoctorCheck CheckWebView2()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "EdgeWebView", "Application");
        return Directory.Exists(root)
            ? new DoctorCheck("WebView2", CheckStatus.Pass, "运行时已安装，可在应用内嵌入 DeepSeek Harness 界面")
            : new DoctorCheck("WebView2", CheckStatus.Warn, "未检测到 WebView2 运行时，应用内嵌入将不可用");
    }
}
