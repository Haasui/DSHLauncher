using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        }
        finally
        {
            IsRunning = false;
        }
    }

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