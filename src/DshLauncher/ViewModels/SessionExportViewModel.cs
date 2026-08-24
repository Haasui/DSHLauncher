using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;
using Microsoft.Win32;

namespace DshLauncher.ViewModels;

/// <summary>会话导出卡（独特）：从 DSH 磁盘转存导出会话为 Markdown（官方无此接口，属薄壳增强）。</summary>
public partial class SessionExportViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public SessionExportViewModel(ISettingsService settings) => _settings = settings;

    public ObservableCollection<SessionPick> Sessions { get; } = new();

    [ObservableProperty]
    private SessionPick? _selectedSession;
    partial void OnSelectedSessionChanged(SessionPick? value) => OnPropertyChanged(nameof(HasSelectedSession));
    public bool HasSelectedSession => SelectedSession is not null;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "选择会话后可导出为 Markdown";

    /// <summary>刷新会话列表（官方 session.list 只读）。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Sessions.Clear();
            var api = new DshApiClient(_settings.Port);
            var items = await api.ListSessionsAsync();
            var archived = (await api.GetArchivedSessionIdsAsync()).ToHashSet();
            foreach (var s in items)
            {
                // 归档会话在 DSH 界面无查看入口、深链也会回退；空白会话无内容；子代理会话非用户对话。均跳过。
                if (s.Blank || s.IsSubagent || archived.Contains(s.SessionId)) continue;
                var title = string.IsNullOrEmpty(s.Title) ? (s.SessionId.Length > 8 ? s.SessionId[..8] : s.SessionId) : s.Title;
                var dot = s.Running ? "🟢" : "⚪";
                Sessions.Add(new SessionPick(s, $"{dot} {title}"));
            }
            if (Sessions.Count == 0)
            {
                SelectedSession = null;
                Status = "没有可导出的会话。";
            }
            else
            {
                SelectedSession = Sessions[0];
                Status = $"找到 {Sessions.Count} 个会话。";
            }
        }
        catch (Exception ex)
        {
            Status = "读取会话失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>导出所选会话为 Markdown：保存对话框 → 读取磁盘转存 → 写入文件。</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy || SelectedSession is not { } pick) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出会话为 Markdown",
            Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
            FileName = Sanitize(pick.Hit.Title ?? pick.Hit.SessionId) + ".md",
            DefaultExt = ".md"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        Status = "正在导出…";
        try
        {
            var r = await Task.Run(() =>
            {
                using var fs = System.IO.File.Create(dlg.FileName);
                return SessionExporter.ExportToMarkdown(pick.Hit.SessionId, pick.Hit.Title, fs);
            });
            Status = r.Ok ? "已导出到 " + dlg.FileName : r.Message;
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '-');
        return s.Length > 40 ? s[..40] : s;
    }
}

/// <summary>导出下拉项：显示标题 + 运行状态。</summary>
public sealed class SessionPick
{
    public SessionPick(DshSession hit, string display) { Hit = hit; Display = display; }
    public DshSession Hit { get; }
    public string Display { get; }
    public override string ToString() => Display;
}
