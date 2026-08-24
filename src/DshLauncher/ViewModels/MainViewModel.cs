using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>主窗口 VM：维护左侧 8 个分页导航 + WebView2 嵌入页。</summary>
public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly EmbedViewModel _embed;
    private object? _extraPage;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    /// <summary>当前显示的页面 VM（嵌入页优先，否则取选中导航项）。</summary>
    public object? CurrentPage => _extraPage ?? SelectedNavItem?.Page;

    /// <summary>左侧导航栏宽度：嵌入 DSH 界面时收起为 0（用户需求）。</summary>
    public GridLength NavColumnWidth => _extraPage == null ? new GridLength(216) : new GridLength(0);

    /// <summary>是否处于嵌入 DSH 界面状态（状态栏据此显示「退出嵌入」）。</summary>
    public bool IsEmbedding => _extraPage != null;

    private void NotifyEmbedState()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(NavColumnWidth));
        OnPropertyChanged(nameof(IsEmbedding));
    }

    [RelayCommand]
    private void ExitEmbed() => NavigateTo<HomeViewModel>();

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        // 用户点击导航项 → 立即退出嵌入页（修复：此前 _extraPage 会一直顶替 CurrentPage）
        _extraPage = null;
        NotifyEmbedState();
    }

    /// <summary>启动页 VM（托盘/快捷操作需要）。</summary>
    public HomeViewModel Home { get; }

    /// <summary>顶部状态栏 VM（Napcat 风格：常显 DSH 状态）。</summary>
    public StatusBarViewModel StatusBar { get; private set; } = null!;

    /// <summary>跨会话搜索面板（Ctrl+F）。</summary>
    public SessionSearchViewModel SessionSearch { get; } = new(AppServices.Settings);

    [RelayCommand]
    private void ToggleSearch() => SessionSearch.ToggleCommand.Execute(null);

    public MainViewModel()
    {
        _embed = new EmbedViewModel(AppServices.Settings, AppServices.Status, this);
        Home = new HomeViewModel(AppServices.Dsh, AppServices.Status, AppServices.Log, AppServices.Settings, this);
        StatusBar = new StatusBarViewModel(this);
        NavItems.Add(new NavItem(Loc.Get("nav.home"), "", Home));
        NavItems.Add(new NavItem(Loc.Get("nav.settings"), "", new SettingsViewModel(AppServices.Settings)));
        NavItems.Add(new NavItem(Loc.Get("nav.doctor"), "", new DoctorViewModel(AppServices.Doctor)));
        NavItems.Add(new NavItem(Loc.Get("nav.update"), "", new UpdateViewModel(AppServices.Update)));
        NavItems.Add(new NavItem(Loc.Get("nav.plugin"), "", new PluginViewModel(AppServices.Plugin)));
        NavItems.Add(new NavItem(Loc.Get("nav.quick"), "", new QuickRefViewModel()));
        NavItems.Add(new NavItem(Loc.Get("nav.log"), "", new LogViewModel(AppServices.Log)));
        NavItems.Add(new NavItem(Loc.Get("nav.about"), "", new AboutViewModel()));

        SelectedNavItem = NavItems[0];

        // 后台检查更新，更新页导航项亮徽标（不打扰）
        _ = CheckUpdateBackgroundAsync();

        // 打开时自动启动（设置项；若端口已开放则视为已在运行）
        FileLog.Write($"MainViewModel ctor: AutoStart={AppServices.Settings.AutoStartOnLaunch} " +
                      $"IsPortOpen={AppServices.Status.IsPortOpen} Port={AppServices.Settings.Port}");
        if (AppServices.Settings.AutoStartOnLaunch && !AppServices.Status.IsPortOpen)
        {
            FileLog.Write("MainViewModel: executing Home.StartCommand");
            Home.StartCommand.Execute(null);
            FileLog.Write("MainViewModel: StartCommand.Execute returned");
        }
    }

    private async Task CheckUpdateBackgroundAsync()
    {
        try
        {
            var sources = await AppServices.Update.GetSourcesAsync();
            var current = Semver.TryParse(sources.Current);
            var target = Semver.TryParse(sources.Latest);
            var available = target is not null && (current is null || target.CompareTo(current) > 0);
            if (available)
            {
                var item = NavItems.FirstOrDefault(n => n.Page is UpdateViewModel);
                if (item is not null) item.Badge = "新";
            }
        }
        catch
        {
            // 后台检查失败静默
        }
    }

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        var item = NavItems.FirstOrDefault(n => n.Page is TViewModel);
        if (item is not null)
        {
            _extraPage = null;
            SelectedNavItem = item;
        }
    }

    public void ShowEmbed()
    {
        // 先置空选中（会触发 OnSelectedNavItemChanged 清掉旧 _extraPage），再设嵌入页并收起导航
        SelectedNavItem = null;
        _extraPage = _embed;
        NotifyEmbedState();
        _ = _embed.OpenAsync();
    }
}
