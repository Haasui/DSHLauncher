using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>插件页 VM：列表 / 安装（npm 或 git）/ 更新 / 隔离。</summary>
public partial class PluginViewModel : ObservableObject
{
    private readonly IPluginService _plugin;

    public ObservableCollection<PluginItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string _installSource = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _status = "点击「刷新」列出已装插件。";

    private int _isolatedCount;

    /// <summary>插件健康徽标：全部健康 / N 个隔离（用户一眼看状态）。</summary>
    public string HealthText
    {
        get
        {
            if (Items.Count == 0 && _isolatedCount == 0) return "未扫描";
            return _isolatedCount == 0 ? "✓ 全部健康" : $"⚠ {_isolatedCount} 个隔离";
        }
    }

    public System.Windows.Media.Brush HealthBrush
        => _isolatedCount == 0 ? StatusBrushes.Green : StatusBrushes.Orange;

    private void RefreshHealth()
    {
        _isolatedCount = Items.Count(i => i.Isolated);
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthBrush));
    }

    public PluginViewModel(IPluginService plugin) => _plugin = plugin;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        Items.Clear();
        try
        {
            var plugins = await _plugin.GetPluginsAsync();
            foreach (var p in plugins) Items.Add(new PluginItemViewModel(p));
            Status = Items.Count == 0 ? "未发现插件。" : $"共 {Items.Count} 个插件。";
            RefreshHealth();
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            RefreshHealth();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallSource))
        {
            Status = "请输入 npm 包名或 git 地址。";
            return;
        }
        Status = "正在安装（可能需要几分钟）…";
        var ok = await _plugin.InstallAsync(InstallSource);
        Status = ok ? $"安装成功：{InstallSource}" : "安装失败，请查看详情。";
        InstallSource = string.Empty;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UpdateAsync(PluginItemViewModel? item)
    {
        if (item is null || item.Isolated) return;
        Status = $"正在更新 {item.Id}…";
        var ok = await _plugin.UpdateAsync(item.Id);
        Status = ok ? $"更新完成：{item.Id}" : $"更新失败：{item.Id}";
        await RefreshAsync();
    }

    // ---- 插件源（npm 搜索 + 一键安装） ----

    [ObservableProperty]
    private string _storeQuery = string.Empty;

    [ObservableProperty]
    private string _storeSort = "stars";

    public string[] StoreSortOptions { get; } = { "最热", "最新" };

    public string SelectedStoreSort
    {
        get => StoreSort == "updated" ? "最新" : "最热";
        set => StoreSort = value == "最新" ? "updated" : "stars";
    }

    /// <summary>市场分类（从结果真实 gitHub topics 聚合，去掉通用标签）。点击按 topic 搜索。</summary>
    public ObservableCollection<string> StoreCategories { get; } = new();

    private static readonly string[] GenericTopics = { "dsh-plugin", "deepseek-harness", "deepseek", "harness", "dsh", "plugin" };

    [RelayCommand]
    private async Task SearchCategoryAsync(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        StoreQuery = "topic:" + category;
        await SearchStoreAsync();
    }

    private void RebuildStoreCategories()
    {
        StoreCategories.Clear();
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in StoreResults)
            foreach (var t in c.Topics)
                if (!GenericTopics.Contains(t, StringComparer.OrdinalIgnoreCase))
                    freq[t] = freq.GetValueOrDefault(t) + 1;
        foreach (var kv in freq.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(10))
            StoreCategories.Add(kv.Key);
    }

    [ObservableProperty]
    private string _storeStatus = "在官方插件源搜索 DeepSeek Harness 插件。";

    [ObservableProperty]
    private bool _isStoreLoading;

    public ObservableCollection<PluginCandidateViewModel> StoreResults { get; } = new();

    private int _storePage = 1;
    private int _storeTotal;
    private bool _storeHasMore;

    public bool HasMoreStoreResults => _storeHasMore;
    public string StoreLoadMoreText => StoreResults.Count == 0 ? "加载更多" : $"加载更多 {StoreResults.Count}/{_storeTotal}";

    [RelayCommand]
    private async Task SearchStoreAsync()
    {
        if (IsStoreLoading) return;
        IsStoreLoading = true;
        StoreResults.Clear();
        StoreStatus = "正在搜索…";
        try
        {
            var res = await AppServices.Store.SearchAllPluginsAsync(StoreQuery, 1, StoreSort);
            var installed = (await _plugin.GetPluginsAsync()).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in res.Items) StoreResults.Add(new PluginCandidateViewModel(c, installed.Contains(c.Id)));
            _storeTotal = res.MarketTotal; _storeHasMore = res.HasMore; _storePage = 1;
            StoreStatus = res.Items.Count == 0 ? "没有结果（检查网络？）" : $"dsh 插件市场共 {res.MarketTotal} 个。";
            OnPropertyChanged(nameof(HasMoreStoreResults)); OnPropertyChanged(nameof(StoreLoadMoreText));
            RebuildStoreCategories();
        }
        catch (Exception ex)
        {
            StoreStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsStoreLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreStoreAsync()
    {
        if (IsStoreLoading || !_storeHasMore) return;
        IsStoreLoading = true;
        StoreStatus = "正在加载更多…";
        try
        {
            var installed = (await _plugin.GetPluginsAsync()).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var res = await AppServices.Store.SearchAllPluginsAsync(StoreQuery, _storePage + 1, StoreSort);
            foreach (var c in res.Items)
            {
                if (StoreResults.Any(x => x.Id == c.Id)) continue;
                StoreResults.Add(new PluginCandidateViewModel(c, installed.Contains(c.Id)));
            }
            _storeTotal = res.MarketTotal; _storeHasMore = res.HasMore; _storePage = res.From;
            StoreStatus = $"dsh 插件市场共 {_storeTotal} 个，已显示 {StoreResults.Count} 个。";
            RebuildStoreCategories();
        }
        catch (Exception ex)
        {
            StoreStatus = "加载失败：" + ex.Message;
        }
        finally
        {
            IsStoreLoading = false;
            OnPropertyChanged(nameof(HasMoreStoreResults));
            OnPropertyChanged(nameof(StoreLoadMoreText));
        }
    }

    [RelayCommand]
    private async Task InstallFromStoreAsync(PluginCandidateViewModel? item)
    {
        if (item is null) return;
        // GitHub 仓库直接以 github:user/repo 安装（官方 dsh plugin add 支持）
        var source = item.Candidate.IsGitHub ? "github:" + item.Id : item.Id;
        Status = $"正在安装 {item.Id}…";
        try
        {
            var ok = await _plugin.InstallAsync(source);
            Status = ok ? $"安装成功：{item.Id}。重启 DeepSeek Harness 后生效。" : $"安装失败：{item.Id}。";
            if (ok) item.MarkInstalled();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenPluginRepo(PluginItemViewModel? item)
    {
        if (item?.RepositoryUrl is { Length: > 0 } url)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task SelfCheckAsync()
    {
        if (IsLoading) return;
        Status = "正在自检插件（损坏插件会自动隔离）…";
        try
        {
            var quarantined = await _plugin.SelfCheckAsync();
            Status = quarantined.Count == 0
                ? "自检完成：插件健康，无损坏。"
                : $"自检完成：{quarantined.Count} 个损坏插件已自动隔离：" + string.Join("、", quarantined);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = "自检失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task FixDepsAsync(PluginItemViewModel? item)
    {
        if (item is null) return;
        Status = $"正在检查 {item.Id} 依赖…";
        try
        {
            Status = await _plugin.GetFixCommandsAsync(item.Id);
        }
        catch (Exception ex)
        {
            Status = "检查失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleIsolateAsync(PluginItemViewModel? item)
    {
        if (item is null) return;
        Status = item.Isolated ? $"正在恢复 {item.Id}…" : $"正在隔离 {item.Id}…";
        var ok = item.Isolated
            ? await _plugin.UnisolateAsync(item.Id)
            : await _plugin.IsolateAsync(item.Id);
        Status = ok ? (item.Isolated ? $"已恢复：{item.Id}" : $"已隔离：{item.Id}") : "操作失败。";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UninstallAsync(PluginItemViewModel? item)
    {
        if (item is null) return;
        Status = $"正在卸载 {item.Id}…";
        try
        {
            var ok = await _plugin.UninstallAsync(item.Id);
            Status = ok ? $"已卸载：{item.Id}。" : $"卸载失败：{item.Id}。";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = "卸载失败：" + ex.Message;
        }
    }
}

/// <summary>插件条目显示包装。</summary>
public sealed class PluginItemViewModel
{
    public PluginItemViewModel(PluginInfo plugin) => Plugin = plugin;

    public PluginInfo Plugin { get; }

    public string Id => Plugin.Id;

    public string Version => Plugin.Version ?? "—";

    public string Description => Plugin.Description ?? string.Empty;

    public bool Isolated => Plugin.Isolated;

    public string IsolateButtonText => Isolated ? "恢复" : "隔离";

    public System.Windows.Media.Brush Brush => Isolated ? StatusBrushes.Orange : StatusBrushes.Blue;

    /// <summary>状态徽标（Napcat 插件「运行中」标风）：已启用/已隔离。</summary>
    public string StatusBadge => Isolated ? "已隔离" : "已启用";

    public System.Windows.Media.Brush BadgeBrush => Isolated ? StatusBrushes.Orange : StatusBrushes.Green;

    /// <summary>插件图标（Napcat 圆形图标风，暂用鲸鱼娘 app-brand）。</summary>
    public string IconUri => "pack://application:,,,/Assets/app-brand.png";

    /// <summary>插件仓库/文档页 URL（读取已装 package.json 的 repository 字段）。</summary>
    private string? _repo;
    public string? RepositoryUrl => _repo ??= ReadRepository();
    public bool HasRepository => !string.IsNullOrEmpty(RepositoryUrl);

    private string? ReadRepository()
    {
        try
        {
            if (string.IsNullOrEmpty(Plugin.InstalledPath)) return null;
            var pj = Path.Combine(Plugin.InstalledPath, "package.json");
            if (!File.Exists(pj)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(pj));
            if (!doc.RootElement.TryGetProperty("repository", out var r)) return null;
            var url = r.ValueKind switch
            {
                JsonValueKind.String => r.GetString(),
                JsonValueKind.Object when r.TryGetProperty("url", out var u) => u.GetString(),
                _ => null,
            };
            return NormalizeRepo(url);
        }
        catch { return null; }
    }

    private static string? NormalizeRepo(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var url = s.Trim();
        if (url.StartsWith("git+", StringComparison.OrdinalIgnoreCase)) url = url[4..];
        if (url.StartsWith("git://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[("git://").Length..];
        if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) url = "https://github.com/" + url[("git@github.com:").Length..];
        if (url.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) url = "https://github.com/" + url[("github:").Length..];
        var hash = url.IndexOf('#'); if (hash >= 0) url = url[..hash];
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url[..^4];
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
    }
}

/// <summary>插件源候选显示包装（带已装标记）。</summary>
public partial class PluginCandidateViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isInstalled;

    public PluginCandidateViewModel(PluginCandidate candidate, bool isInstalled)
    {
        Candidate = candidate;
        _isInstalled = isInstalled;
    }

    public PluginCandidate Candidate { get; }

    public string Id => Candidate.Id;

    public string Version => Candidate.Version;

    public string Description => Candidate.Description ?? string.Empty;

    public string Author => Candidate.Author ?? "";

    public bool IsInstalled
    {
        get => _isInstalled;
        private set => SetProperty(ref _isInstalled, value);
    }

    public string InstallButtonText => IsInstalled ? "已安装 ✓" : "安装";

    public bool IsGitHub => Candidate.IsGitHub;

    public string SourceLabel => IsGitHub ? "GitHub 仓库" : "npm 包";

    /// <summary>版本徽标：npm 显示 v版本，GitHub 显示来源（无版本 tag 信息）。</summary>
    public string VersionBadge => IsGitHub ? SourceLabel : "v" + Version;

    public bool CanInstall => !IsInstalled;

    public int Stargazers => Candidate.Stargazers;
    public bool HasStars => Stargazers > 0;
    public string StarText => Stargazers > 0 ? $"★ {Stargazers}" : "";

    public IReadOnlyList<string> Topics => Candidate.Topics ?? Array.Empty<string>();

    private static readonly string[] GenericTags = { "dsh-plugin", "deepseek-harness", "deepseek", "harness", "dsh", "plugin" };

    /// <summary>卡片上展示的真实分类标签（去掉通用/无关的，最多 4 个）。</summary>
    public IReadOnlyList<string> TopicTags =>
        Topics.Where(t => !GenericTags.Contains(t, StringComparer.OrdinalIgnoreCase)).Take(4).ToList();

    /// <summary>插件仓库/文档页 URL（GitHub 源来自 html_url，npm 来自 registry repository）。</summary>
    public string? RepositoryUrl => Candidate.Repository;

    public bool HasRepository => !string.IsNullOrEmpty(Candidate.Repository);

    [RelayCommand]
    private void OpenRepo()
    {
        if (!string.IsNullOrEmpty(Candidate.Repository))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Candidate.Repository) { UseShellExecute = true });
    }

    public void MarkInstalled()
    {
        IsInstalled = true;
        OnPropertyChanged(nameof(InstallButtonText));
        OnPropertyChanged(nameof(CanInstall));
    }
}
