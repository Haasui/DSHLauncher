using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>
/// 顶部状态栏 VM：常显 DSH 状态 + 端口 + 主题切换 + 刷新；嵌入 DSH 界面时提供「停止/退出嵌入」。
/// 代理 MainViewModel/HomeViewModel 状态，避免双源同步。
/// </summary>
public partial class StatusBarViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly HomeViewModel _home;
    private readonly string[] _themes = { "system", "dark", "light" };
    private int _themeIndex;

    public StatusBarViewModel(MainViewModel main)
    {
        _main = main;
        _home = main.Home;
        _home.PropertyChanged += (_, _) => NotifyAll();
        _main.PropertyChanged += (_, _) => NotifyAll();
    }

    public Brush DshBrush => _home.StatusBrush;
    public string DshText => _home.StatusText;
    public string DshDetail => _home.StatusDetail;
    public int Port => _home.Port;
    public bool DshRunning => _home.State == DshState.Running;

    /// <summary>是否处于嵌入 DSH 界面（状态栏据此显示「停止/退出嵌入」）。</summary>
    public bool IsEmbedding => _main.IsEmbedding;

    public IAsyncRelayCommand RefreshCommand => _home.RefreshOverviewCommand;
    public ICommand StopCommand => _home.StopCommand;
    public ICommand ExitEmbedCommand => _main.ExitEmbedCommand;
    public bool CanStop => _home.CanStop;
    public bool CanExit => _main.IsEmbedding;

    public string AppVersion => DshLauncher.App.Version;

    /// <summary>DSH 官方主题标签（跟随系统/深/浅）。</summary>
    [ObservableProperty]
    private string _themeLabel = "主题·跟随系统";

    /// <summary>一键切换 DSH 官方 ui-theme（system → dark → light 循环）。走官方 settings.mutate + CAS。</summary>
    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var api = new DshApiClient(Port);
        long? revision = null;
        try
        {
            var rows = await api.DescribeSettingsAsync();
            var theme = rows.FirstOrDefault(r => string.Equals(r.Ns, "ui-theme", StringComparison.OrdinalIgnoreCase));
            if (theme is not null) revision = theme.Revision;
        }
        catch { }

        _themeIndex = (_themeIndex + 1) % _themes.Length;
        var pref = _themes[_themeIndex];
        try
        {
            await api.MutateSettingsAsync("ui-theme", "set", new[] { "preference" }, pref, revision);
            ThemeLabel = pref == "system" ? "主题·跟随系统" : pref == "dark" ? "主题·深色" : "主题·浅色";
        }
        catch (Exception ex)
        {
            ThemeLabel = "主题切换失败";
            FileLog.Write($"ToggleTheme FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(DshBrush));
        OnPropertyChanged(nameof(DshText));
        OnPropertyChanged(nameof(DshDetail));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(DshRunning));
        OnPropertyChanged(nameof(IsEmbedding));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanExit));
    }
}
