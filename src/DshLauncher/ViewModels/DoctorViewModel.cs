using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;
using Microsoft.Win32;

namespace DshLauncher.ViewModels;

/// <summary>诊断页 VM：一键检测 + 组合配置树查看（官方 --dump-config）。</summary>
public partial class DoctorViewModel : ObservableObject
{
    private readonly IDoctorService _doctor;

    public ObservableCollection<DoctorItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _reportStatus = string.Empty;

    [ObservableProperty]
    private string _healthSummary = string.Empty;

    [ObservableProperty]
    private string _configDumpText = string.Empty;

    public bool HasConfigDump => !string.IsNullOrEmpty(ConfigDumpText);

    /// <summary>系统内存使用率 %（环形进度）。</summary>
    [ObservableProperty]
    private double _memoryPercent;

    /// <summary>系统盘剩余 %（环形进度）。</summary>
    [ObservableProperty]
    private double _diskPercent;

    /// <summary>系统盘剩余 GB（环形图副文字）。</summary>
    [ObservableProperty]
    private string _diskFree = "--";

    public DoctorViewModel(IDoctorService doctor)
    {
        _doctor = doctor;
        LoadSystemStats();
    }

    /// <summary>读取系统内存/磁盘使用率（Napcat 环形图数据源）。</summary>
    private void LoadSystemStats()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                MemoryPercent = mem.dwMemoryLoad;
        }
        catch { }
        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                var free = drive.AvailableFreeSpace;
                DiskPercent = free * 100.0 / drive.TotalSize;
                DiskFree = $"{free / (1024.0 * 1024 * 1024):0.0} GB";
            }
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Items.Clear();
        try
        {
            var checks = await _doctor.RunAllAsync();
            foreach (var c in checks) Items.Add(new DoctorItemViewModel(c));
            HealthSummary = BuildSummary(checks);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string BuildSummary(IReadOnlyList<DoctorCheck> checks)
    {
        var pass = checks.Count(c => c.Status == CheckStatus.Pass);
        var warn = checks.Count(c => c.Status == CheckStatus.Warn);
        var fail = checks.Count(c => c.Status == CheckStatus.Fail);
        return $"共 {checks.Count} 项 · 通过 {pass} · 警告 {warn} · 失败 {fail}";
    }

    [RelayCommand]
    private async Task FixAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Items.Clear();
        try
        {
            var checks = await _doctor.RunAllAsync();
            foreach (var c in checks) Items.Add(new DoctorItemViewModel(c));
            HealthSummary = BuildSummary(checks);
            var problems = checks.Where(c => c.Status == CheckStatus.Fail || c.Status == CheckStatus.Warn).ToList();
            if (problems.Count == 0)
            {
                ReportStatus = "检测完毕：一切正常，无需修复。";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"检测到 {problems.Count} 个问题，点「是」自动修复能修的：");
            foreach (var p in problems) sb.AppendLine($"  · {p.Name}：{Brief(p.Detail)}");
            sb.AppendLine();
            sb.AppendLine("可自动修复：dsh 重装、WebView2 下载、Node/npm 下载。其余请参考提示。");
            var yes = System.Windows.MessageBox.Show(sb.ToString(), "一键修复", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (yes != System.Windows.MessageBoxResult.Yes) { ReportStatus = "已取消修复。"; return; }
            var done = new List<string>();
            if (problems.Any(p => p.Name == "dsh"))
            {
                var r = await CommandRunner.RunAsync("npm install -g @deepseek-ai/dsh@latest", TimeSpan.FromMinutes(5));
                done.Add(r.ExitCode == 0 ? "dsh 已安装/更新" : "dsh 安装失败：" + Brief(r.Output));
            }
            if (problems.Any(p => p.Name == "WebView2"))
            {
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
                done.Add("已打开 WebView2 下载页");
            }
            if (problems.Any(p => p.Name is "npm" or "Node.js"))
            {
                Process.Start(new ProcessStartInfo("https://nodejs.org/") { UseShellExecute = true });
                done.Add("已打开 Node.js 下载页");
            }
            if (problems.Any(p => p.Name == "网络"))
                done.Add("网络异常，请在设置页配置 npm 镜像");
            if (problems.Any(p => p.Name.Contains("端口")))
                done.Add("端口占用，请在设置页更换端口");
            ReportStatus = (done.Count == 0 ? "无可自动修复项。" : string.Join("；", done)) + "。请重新「一键检测」查看。";
            var re = await _doctor.RunAllAsync();
            Items.Clear();
            foreach (var c in re) Items.Add(new DoctorItemViewModel(c));
            HealthSummary = BuildSummary(re);
        }
        finally { IsRunning = false; }
    }

    private static string Brief(string s)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > 40 ? s[..40] + "…" : s);

    [RelayCommand]
    private async Task DumpConfigAsync()
    {
        ConfigDumpText = "正在读取组合配置…";
        OnPropertyChanged(nameof(HasConfigDump));
        var r = await CommandRunner.RunAsync("dsh web --dump-config", TimeSpan.FromSeconds(30));
        var head = r.Output;
        if (head.Length > 20000) head = head[..20000] + "\n…（过长已截断）";
        ConfigDumpText = r.ExitCode == 0 && head.Length > 0 ? head : "读取失败：" + (string.IsNullOrEmpty(r.Output) ? "未知错误" : r.Output);
        OnPropertyChanged(nameof(HasConfigDump));
    }

    /// <summary>体检报告导出：重新跑一次检测，写 Markdown 或 JSON 到用户所选文件。</summary>
    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        ReportStatus = "正在检测…";
        try
        {
            var checks = await _doctor.RunAllAsync();
            Items.Clear();
            foreach (var c in checks) Items.Add(new DoctorItemViewModel(c));

            var dlg = new SaveFileDialog
            {
                Title = "导出体检报告",
                Filter = "Markdown 报告 (*.md)|*.md|JSON 数据 (*.json)|*.json",
                FileName = "dsh-report",
                DefaultExt = ".md",
            };
            if (dlg.ShowDialog() != true) { ReportStatus = "已取消。"; return; }

            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var content = ext == ".json" ? ToReportJson(checks) : ToMarkdown(checks);
            await File.WriteAllTextAsync(dlg.FileName, content);
            ReportStatus = "已导出：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            ReportStatus = "导出失败：" + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string ToMarkdown(IReadOnlyList<DoctorCheck> checks)
    {
        var pass = checks.Count(c => c.Status == CheckStatus.Pass);
        var warn = checks.Count(c => c.Status == CheckStatus.Warn);
        var fail = checks.Count(c => c.Status == CheckStatus.Fail);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# DeepSeek Harness 体检报告");
        sb.AppendLine();
        sb.AppendLine($"> 生成时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 共 {checks.Count} 项（通过 {pass} / 警告 {warn} / 失败 {fail}）");
        sb.AppendLine();
        foreach (var c in checks)
        {
            var icon = c.Status switch { CheckStatus.Pass => "✅", CheckStatus.Warn => "⚠️", CheckStatus.Fail => "❌", _ => "ℹ️" };
            sb.AppendLine($"- {icon} **{c.Name}**：{c.Detail}");
        }
        return sb.ToString();
    }

    private static string ToReportJson(IReadOnlyList<DoctorCheck> checks)
    {
        var items = checks.Select(c => new { name = c.Name, status = c.Status.ToString(), detail = c.Detail });
        var payload = new { generatedAt = DateTime.Now, name = "DeepSeek Harness 体检报告", items };
        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>单项检测的显示包装。</summary>
public sealed class DoctorItemViewModel
{
    public DoctorItemViewModel(DoctorCheck check) => Check = check;

    public DoctorCheck Check { get; }

    public string Name => Check.Name;

    public string StatusText => Check.Status switch
    {
        CheckStatus.Pass => "通过",
        CheckStatus.Warn => "警告",
        CheckStatus.Fail => "失败",
        _ => "信息",
    };

    public string Detail => Check.Detail;

    public System.Windows.Media.Brush Brush => Check.Status switch
    {
        CheckStatus.Pass => StatusBrushes.Green,
        CheckStatus.Warn => StatusBrushes.Orange,
        CheckStatus.Fail => StatusBrushes.Red,
        _ => StatusBrushes.Gray,
    };
}