using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>设置页 VM：启动器设置 + DSH 官方设置中心（settings.describe 只读，P-官方对齐）。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        _port = _settings.Port;
        _extraArgs = _settings.ExtraArgs ?? string.Empty;
        _patchFile = _settings.PatchFile ?? string.Empty;
        _autoStartOnLaunch = _settings.AutoStartOnLaunch;
        _startWithWindows = _settings.StartWithWindows;
        _minimizeToTray = _settings.MinimizeToTray;
        _openInBrowser = _settings.OpenInBrowser;
        _autoRestartOnCrash = _settings.AutoRestartOnCrash;
        _theme = _settings.Theme;
        _npmRegistry = _settings.NpmRegistry ?? string.Empty;
        _quoteApiUrl = _settings.QuoteApiUrl ?? string.Empty;
        _ = LoadModelConfigAsync();
    }

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private string _extraArgs = string.Empty;

    [ObservableProperty]
    private string _patchFile = string.Empty;

    [ObservableProperty]
    private bool _autoStartOnLaunch;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _openInBrowser = true;

    [ObservableProperty]
    private bool _autoRestartOnCrash = true;

    public string[] ThemeOptions { get; } = { "浅色", "深色" };

    public string SelectedThemeDisplay
    {
        get => Theme == "dark" ? "深色" : "浅色";
        set
        {
            Theme = value == "深色" ? "dark" : "light";
            OnPropertyChanged();
            ThemeManager.Apply(ThemeManager.Resolve(Theme)); // 实时热切
        }
    }

    [ObservableProperty]
    private string _theme = "light";

    [ObservableProperty]
    private string _npmRegistry = string.Empty;

    [ObservableProperty]
    private string _quoteApiUrl = string.Empty;

    // ---- DSH 默认模型（agent-default-model 图形化配置） ----
    public ObservableCollection<string> ProviderSuggestions { get; } = new();
    public ObservableCollection<string> ModelSuggestions { get; } = new();
    public string[] ReasoningEfforts { get; } = { "low", "medium", "high" };

    [ObservableProperty]
    private string _provider = "";

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    private string _reasoningEffort = "high";

    [ObservableProperty]
    private bool _isModelConfigWritable;

    [ObservableProperty]
    private bool _isModelConfigBusy;

    [ObservableProperty]
    private string _modelConfigStatus = "点击「读取」查看当前默认模型。";

    private long _modelRevision;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string ConfigPath => _settings.ConfigPath;

    // ---- DSH 官方设置（host 视角，只读） ----
    public ObservableCollection<DshSettingsItemViewModel> DshSettings { get; } = new();

    [ObservableProperty]
    private string _dshSettingsStatus = "点击「刷新」查看 DeepSeek Harness 运行配置。";

    [ObservableProperty]
    private bool _isDshSettingsLoading;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Port is < 1 or > 65535)
        {
            StatusMessage = "端口必须在 1–65535 之间。";
            return;
        }

        _settings.Port = Port;
        _settings.ExtraArgs = ExtraArgs;
        _settings.PatchFile = PatchFile;
        _settings.AutoStartOnLaunch = AutoStartOnLaunch;
        _settings.StartWithWindows = StartWithWindows;
        _settings.MinimizeToTray = MinimizeToTray;
        _settings.OpenInBrowser = OpenInBrowser;
        _settings.AutoRestartOnCrash = AutoRestartOnCrash;
        _settings.Theme = Theme;
        ThemeManager.Apply(ThemeManager.Resolve(Theme)); // 保存后应用（保留界面即时生效）
        _settings.NpmRegistry = string.IsNullOrWhiteSpace(NpmRegistry) ? null : NpmRegistry.Trim();
        _settings.QuoteApiUrl = string.IsNullOrWhiteSpace(QuoteApiUrl) ? null : QuoteApiUrl.Trim();
        try
        {
            await _settings.SaveAsync();
            AppServices.Status.Port = Port; // 同步状态轮询端口
            StatusMessage = "已保存。";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshDshSettingsAsync()
    {
        if (IsDshSettingsLoading) return;
        IsDshSettingsLoading = true;
        DshSettings.Clear();
        DshSettingsStatus = "正在读取 DeepSeek Harness 配置…";
        try
        {
            var api = new DshApiClient(Port);
            var rows = await api.DescribeSettingsAsync();
            foreach (var row in rows) DshSettings.Add(new DshSettingsItemViewModel(row));
            DshSettingsStatus = rows.Count == 0
                ? "没有读取到配置（DeepSeek Harness 未运行？）。"
                : $"已读取 {rows.Count} 项配置（DeepSeek Harness 运行中，仅查看）。";
        }
        catch (Exception ex)
        {
            DshSettingsStatus = "读取失败：" + ex.Message + "（需 DeepSeek Harness 正在运行）。";
        }
        finally
        {
            IsDshSettingsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadModelConfigAsync()
    {
        if (IsModelConfigBusy) return;
        IsModelConfigBusy = true;
        ModelConfigStatus = "正在读取…";
        try
        {
            var m = await new DshApiClient(Port).GetDefaultModelAsync();
            if (m is null)
            {
                ModelConfigStatus = "读取失败：DeepSeek Harness 未运行？";
                IsModelConfigWritable = false;
                return;
            }
            Provider = m.Provider;
            Model = m.Model;
            ReasoningEffort = string.IsNullOrEmpty(m.ReasoningEffort) ? "high" : m.ReasoningEffort;
            _modelRevision = m.Revision;
            IsModelConfigWritable = m.Writable;

            ProviderSuggestions.Clear();
            foreach (var p in new[] { "deepseek-official", "deepseek", "pi-ai", "openai", "anthropic", "google" })
                if (p != Provider) ProviderSuggestions.Add(p);
            if (Provider.Length > 0) ProviderSuggestions.Insert(0, Provider);

            ModelSuggestions.Clear();
            foreach (var mo in new[] { "deepseek-v4-flash-vision-exp", "deepseek-v4-flash", "deepseek-reasoner", "deepseek-chat" })
                if (mo != Model) ModelSuggestions.Add(mo);
            if (Model.Length > 0) ModelSuggestions.Insert(0, Model);

            ModelConfigStatus = $"当前：{Provider} / {Model}（推理强度 {ReasoningEffort}）";
        }
        catch (Exception ex)
        {
            ModelConfigStatus = "读取失败：" + ex.Message;
        }
        finally
        {
            IsModelConfigBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveModelConfigAsync()
    {
        if (IsModelConfigBusy) return;
        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(Model))
        {
            ModelConfigStatus = "服务商 / 模型 不能为空。";
            return;
        }
        var ok = MessageBox.Show(
            $"将默认模型设置为\n{Provider} / {Model}\n推理强度：{ReasoningEffort}\n\n将写回运行中 DeepSeek Harness 的配置，确定？",
            "确认修改默认模型", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        IsModelConfigBusy = true;
        ModelConfigStatus = "正在写回…";
        try
        {
            var success = await new DshApiClient(Port).SetDefaultModelAsync(Provider, Model, ReasoningEffort, _modelRevision);
            ModelConfigStatus = success ? "已保存生效。" : "保存失败：DeepSeek Harness 拒绝了该设置（可能只读或值无效）。";
            if (success) await LoadModelConfigAsync();
        }
        catch (Exception ex)
        {
            ModelConfigStatus = "保存失败：" + ex.Message;
        }
        finally
        {
            IsModelConfigBusy = false;
        }
    }
}

/// <summary>官方设置单命名空间的显示包装。</summary>
public sealed class DshSettingsItemViewModel
{
    public DshSettingsItemViewModel(DshSettingsNamespace row) => Row = row;

    public DshSettingsNamespace Row { get; }

    public string Name => Row.Ns;

    public string Applies => Row.Applies;

    public string Revision => Row.Revision.ToString();

    public string Value => Row.ValueJson;

    public string User => Row.UserJson;
}