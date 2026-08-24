using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>跨会话搜索面板（Ctrl+F）：官方 session.search，结果可复制/跳转。</summary>
public partial class SessionSearchViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public ObservableCollection<SearchHitViewModel> Results { get; } = new();

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _status = "输入关键词，跨会话搜索对话内容…";

    [ObservableProperty]
    private bool _open;

    public SessionSearchViewModel(ISettingsService settings) => _settings = settings;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsSearching || string.IsNullOrWhiteSpace(Query)) return;
        IsSearching = true;
        Results.Clear();
        Status = "正在搜索…";
        try
        {
            var hits = await new DshApiClient(_settings.Port).SearchSessionsAsync(Query.Trim());
            if (hits.Count == 0) Status = "没有匹配结果。";
            else foreach (var h in hits) Results.Add(new SearchHitViewModel(h));
            Status = hits.Count == 0 ? "没有匹配结果。" : $"找到 {hits.Count} 条。";
        }
        catch (DshApiException ex)
        {
            Status = ex.Message.Contains("disabled") ? "当前 DeepSeek Harness 配置未启用跨会话搜索。" : "搜索失败：" + ex.Message;
        }
        catch (Exception ex)
        {
            Status = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Toggle() => Open = !Open;

    [RelayCommand]
    private void Close() => Open = false;

    [RelayCommand]
    private void CopyResult(SearchHitViewModel? item)
    {
        if (item is null) return;
        try { Clipboard.SetText(item.Snippet); Status = "已复制该结果。"; } catch { }
    }

    [RelayCommand]
    private void OpenResult(SearchHitViewModel? item)
    {
        if (item is null) return;
        try
        {
            var url = $"http://127.0.0.1:{_settings.Port}/?session={Uri.EscapeDataString(item.SessionId)}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>单条搜索命中显示包装。</summary>
public sealed class SearchHitViewModel
{
    public SearchHitViewModel(DshSearchHit hit) => Hit = hit;
    public DshSearchHit Hit { get; }
    public string SessionId => Hit.SessionId;
    public string Snippet => Hit.Snippet;
}
