using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>更新页 VM：三源版本（当前 / npm latest / npm @next），选渠道一键更新。</summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _update;

    public UpdateViewModel(IUpdateService update) => _update = update;

    public string[] ChannelOptions { get; } = { "正式版", "预览版" };

    public string SelectedChannelDisplay
    {
        get => SelectedChannel == "next" ? "预览版" : "正式版";
        set => SelectedChannel = value == "预览版" ? "next" : "latest";
    }

    [ObservableProperty]
    private string _selectedChannel = "latest";

    [ObservableProperty]
    private string _currentVersion = "--";

    [ObservableProperty]
    private string _latestVersion = "--";

    [ObservableProperty]
    private string _nextVersion = "--";

    [ObservableProperty]
    private string _status = "点击「检查更新」对比本地与各渠道最新版本。";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isUpdating;

    public bool UpdateAvailable { get; private set; }

    public string UpdateAvailableText { get; private set; } = "--";

    public bool CanUpdate => UpdateAvailable && !IsUpdating && !IsChecking;

    // ★ 启动器自更新（launcher 自己的 GitHub release）
    [ObservableProperty]
    private string _launcherCurrent = LauncherUpdater.CurrentVersion();

    [ObservableProperty]
    private string _launcherLatest = "--";

    [ObservableProperty]
    private string _launcherStatus = "未检查。发布首个 GitHub release 后即可检测更新。";

    [ObservableProperty]
    private bool _isLauncherBusy;

    [ObservableProperty]
    private bool _launcherCanUpdate;

    // ★ 每源独立可安装判断（latest/next 可一键）
    public bool CanInstallLatest => !IsUpdating && !IsChecking && IsNewer(LatestVersion, CurrentVersion);
    public bool CanInstallNext   => !IsUpdating && !IsChecking && IsNewer(NextVersion,   CurrentVersion);

    partial void OnSelectedChannelChanged(string value) => Recompute();
    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanInstallLatest));
        OnPropertyChanged(nameof(CanInstallNext));
    }
    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanInstallLatest));
        OnPropertyChanged(nameof(CanInstallNext));
    }

    private static bool IsNewer(string? candidate, string? current)
    {
        var c = Semver.TryParse(candidate);
        var cu = Semver.TryParse(current);
        return c is not null && (cu is null || c.CompareTo(cu) > 0);
    }

    private void Recompute()
    {
        var current = Semver.TryParse(CurrentVersion);
        var target = Semver.TryParse(SelectedChannel == "next" ? NextVersion : LatestVersion);
        UpdateAvailable = target is not null && (current is null || target.CompareTo(current) > 0);
        UpdateAvailableText = target is null
            ? "目标渠道版本未知"
            : UpdateAvailable ? $"有更新：{target}（当前 {CurrentVersion}）" : $"已是最新（{target}）";
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateAvailableText));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanInstallLatest));
        OnPropertyChanged(nameof(CanInstallNext));
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        Status = "正在检查…";
        try
        {
            var s = await _update.GetSourcesAsync();
            CurrentVersion = s.Current ?? "未知";
            LatestVersion = s.Latest ?? "未知";
            NextVersion = s.Next ?? "未知";
            Recompute();
            Status = "检查完成。";
        }
        catch (Exception ex)
        {
            Status = "检查失败：" + ex.Message;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (!UpdateAvailable || IsUpdating) return;
        IsUpdating = true;
        try
        {
            var progress = new Progress<string>(s => Status = s);
            var ok = await _update.UpdateToAsync(SelectedChannel, progress);
            if (ok) Status += " 请重新「检查更新」确认版本。";
        }
        catch (Exception ex)
        {
            Status = "更新失败：" + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    // ★ 每源一键安装（绕过渠道下拉，latest/next 直接装）
    [RelayCommand]
    private Task InstallLatestAsync() => InstallChannelAsync("latest");

    [RelayCommand]
    private Task InstallNextAsync() => InstallChannelAsync("next");

    private static string ChannelName(string channel) => channel == "next" ? "预览版" : "正式版";

    private async Task InstallChannelAsync(string channel)
    {
        if (IsUpdating) return;
        IsUpdating = true;
        try
        {
            var name = ChannelName(channel);
            Status = $"开始安装 {name}…";
            var progress = new Progress<string>(s => Status = s);
            var ok = await _update.UpdateToAsync(channel, progress);
            Status = ok
                ? $"已请求安装 {name}。请重新「检查更新」确认版本。"
                : $"{name} 安装失败，查看上面状态。";
        }
        catch (Exception ex)
        {
            Status = $"{ChannelName(channel)} 安装失败：" + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task CheckLauncherUpdateAsync()
    {
        if (IsLauncherBusy) return;
        IsLauncherBusy = true;
        LauncherStatus = "正在检查 launcher 更新…";
        try
        {
            var rel = await LauncherUpdater.CheckAsync();
            LauncherLatest = rel.Tag ?? "未知";
            LauncherCanUpdate = rel.UpdateAvailable && !string.IsNullOrEmpty(rel.DownloadUrl);
            LauncherStatus = rel.UpdateAvailable
                ? $"有新版本：{rel.Tag}（当前 {LauncherCurrent}）"
                : $"已是最新（{rel.Tag}，当前 {LauncherCurrent}）";
        }
        catch (LauncherUpdateException ex)
        {
            LauncherLatest = ex.RepoResponded ? "暂无发布版本" : "不可达";
            LauncherCanUpdate = false;
            LauncherStatus = ex.RepoResponded
                ? "该仓库在 GitHub 上暂未发布版本（创建第一个 release 后可检测更新）。"
                : "无法连接 GitHub，请检查网络后重试。";
        }
        catch (Exception ex)
        {
            LauncherStatus = "检查失败：" + ex.Message;
        }
        finally
        {
            IsLauncherBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        if (!LauncherCanUpdate || IsLauncherBusy) return;
        var ok = MessageBox.Show(
            $"检测到 launcher 新版本 {LauncherLatest}。\n将下载并替换当前启动器（需退出重启）。\n\n立即更新？",
            "更新启动器", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        IsLauncherBusy = true;
        LauncherStatus = "正在下载…";
        try
        {
            var rel = await LauncherUpdater.CheckAsync();
            if (rel is null || string.IsNullOrEmpty(rel.DownloadUrl))
            {
                LauncherStatus = "未获取到下载地址，无法更新。";
                return;
            }
            var progress = new Progress<string>(s => LauncherStatus = "已下载 " + s);
            var newExe = await LauncherUpdater.DownloadAsync(rel.DownloadUrl, progress);
            LauncherStatus = "正在替换并重启…";
            LauncherUpdater.ScheduleReplace(newExe);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LauncherStatus = "更新失败：" + ex.Message;
            IsLauncherBusy = false;
        }
    }
}