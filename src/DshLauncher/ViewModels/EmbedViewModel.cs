using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;
using System.Windows.Threading;

namespace DshLauncher.ViewModels;

/// <summary>WebView2 嵌入页 VM：DSH 运行时加载 http://127.0.0.1:port，否则显示占位。</summary>
public partial class EmbedViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IStatusMonitor _monitor;
    private readonly INavigationService _nav;
    private readonly Dispatcher _ui;

    public EmbedViewModel(ISettingsService settings, IStatusMonitor monitor, INavigationService nav)
    {
        _settings = settings;
        _monitor = monitor;
        _nav = nav;
        _ui = Dispatcher.CurrentDispatcher;
        // 嵌入页常驻 DSH 状态：DSH 死了就回退占位（无需用户手动刷新）
        _monitor.StatusChanged += OnStatusChanged;
    }

    [ObservableProperty]
    private string? _url;

    [ObservableProperty]
    private bool _showEmbed;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>进入页面时刷新：DSH 是否运行决定显示嵌入还是占位。</summary>
    public async Task OpenAsync()
    {
        bool open;
        try { open = await _monitor.CheckAsync(); } catch { open = false; }
        ApplyState(open);
    }

    /// <summary>嵌入初始化失败（如缺 WebView2 运行时）时由视图调用。</summary>
    public void SetError(string message)
    {
        ShowEmbed = false;
        StatusText = message;
    }

    private void OnStatusChanged(object? sender, bool open)
        => _ui.InvokeAsync(() => ApplyState(open));

    private void ApplyState(bool dshRunning)
    {
        if (dshRunning)
        {
            if (!ShowEmbed)
            {
                // 不硬编码 URL：走 DshApiClient 获取接口基础 URL
                _ = ResolveUrlAsync();
                ShowEmbed = true;
                StatusText = string.Empty;
            }
        }
        else
        {
            if (ShowEmbed)
            {
                ShowEmbed = false;
                Url = null;
                StatusText = "DeepSeek Harness 已停止。请到「启动」页重新启动。";
            }
        }
    }

    private async Task ResolveUrlAsync()
    {
        var api = new DshApiClient(_settings.Port);
        var url = await api.GetInterfaceUrlAsync();
        Url = url ?? $"http://127.0.0.1:{_settings.Port}"; // fallback 走配置端口
    }

    [RelayCommand]
    private void GotoHome() => _nav.NavigateTo<HomeViewModel>();
}
