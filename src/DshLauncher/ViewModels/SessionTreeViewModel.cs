using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>
/// 工作区会话树看板：官方 workspace.list（工作区→会话 id）+ session.list（标题/运行态）。
/// 以「工作区 → 会话」两级树可视化；若运行中的会话含 subagent 谱系投影则标注。
/// </summary>
public partial class SessionTreeViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public SessionTreeViewModel(ISettingsService settings) => _settings = settings;

    public ObservableCollection<WorkspaceNode> Workspaces { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "加载工作区会话树…";

    /// <summary>刷新：拉取 workspace.list + session.list 组装两级树。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在加载…";
        try
        {
            var api = new DshApiClient(_settings.Port);
            var workspaces = await api.ListWorkspacesAsync();
            var sessions = await api.ListSessionsAsync();
            var archived = (await api.GetArchivedSessionIdsAsync()).ToHashSet();
            // 归档会话 DSH 界面无查看入口；空白会话无内容；子代理会话非用户对话 → 均不进树
            sessions = sessions.Where(s => !s.Blank && !s.IsSubagent && !archived.Contains(s.SessionId)).ToList();
            var byId = sessions.ToDictionary(s => s.SessionId, s => s);

            Workspaces.Clear();
            int totalSessions = 0;

            // 有工作区归属的会话
            var placed = new HashSet<string>();
            foreach (var w in workspaces)
            {
                var node = new WorkspaceNode(w.Title, w.Path);
                foreach (var sid in w.SessionIds)
                {
                    if (sid.Length == 0 || byId.TryGetValue(sid, out var s) is false) continue;
                    node.Sessions.Add(new SessionLeaf(s));
                    placed.Add(sid);
                    totalSessions++;
                }
                Workspaces.Add(node);
            }

            // 未归属任何工作区的会话，按 cwd 兜底分到独立节点
            var orphanGroups = sessions.Where(s => !placed.Contains(s.SessionId)).GroupBy(s => s.Cwd);
            foreach (var g in orphanGroups)
            {
                var node = new WorkspaceNode(string.IsNullOrEmpty(g.Key) ? "(无工作区)" : null, g.Key);
                foreach (var s in g) node.Sessions.Add(new SessionLeaf(s));
                Workspaces.Add(node);
            }

            // 总会话数 = 各节点叶子之和（工作区 + 兜底孤儿），保证与树中显示一致
            totalSessions = Workspaces.Sum(n => n.Sessions.Count);

            if (Workspaces.Count == 0) Status = "暂无工作区。";
            else
            {
                var running = sessions.Count(s => s.Running);
                Status = $"{Workspaces.Count} 个工作区 · 共 {totalSessions} 个会话（运行中 {running}）";
            }
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>工作区节点：标题/路径 + 会话叶子。</summary>
public sealed class WorkspaceNode
{
    public WorkspaceNode(string? title, string path)
    {
        Title = string.IsNullOrEmpty(title) ? PathName(path) : title;
        Path = path;
    }

    private static string PathName(string p)
    {
        if (string.IsNullOrEmpty(p)) return "(未知工作区)";
        var idx = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
        return idx >= 0 ? p[(idx + 1)..] : p;
    }

    public string Title { get; }
    public string Path { get; }
    public ObservableCollection<SessionLeaf> Sessions { get; } = new();
    public string CountLabel => Sessions.Count + " 个";
}

/// <summary>会话叶子：标题/运行态/短 id/工作区。</summary>
public sealed class SessionLeaf
{
    public SessionLeaf(DshSession s) => Session = s;

    public DshSession Session { get; }
    public string Title => string.IsNullOrEmpty(Session.Title) ? "(无标题)" : Session.Title;
    public string ShortId => Session.SessionId.Length > 8 ? Session.SessionId[..8] : Session.SessionId;
    public bool IsRunning => Session.Running;
    public string Cwd => Session.Cwd;
    public string AgentPreset => Session.AgentPreset;
    public string StateDot => Session.Running ? "🟢" : "⚪";
}
