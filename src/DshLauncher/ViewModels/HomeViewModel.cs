using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Core;

namespace DshLauncher.ViewModels;

/// <summary>DSH 运行状态。</summary>
public enum DshState
{
    NotRunning,
    Starting,
    Running,
    Error,
}

/// <summary>
/// 启动页 VM：状态徽章 + 变身大按钮（未运行→启动 / 启动中→禁用 / 已运行→打开界面 / 异常→前往诊断）。
/// 接入 DshService + StatusMonitor；将「打开 DSH 界面」由系统浏览器改为 WebView2 嵌入。
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly IDshService _dsh;
    private readonly IStatusMonitor _monitor;
    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly INavigationService _nav;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _uptimeTimer;
    private bool _isStopping;
    private bool _readyNotified;
    private bool _adopted;
    private DshHostInfo? _adoptedHost;

    // 守护模式：DSH 异常退出的自动重启尝试计数/时间
    private int _autoRestartCount;
    private DateTime _lastAutoRestart = DateTime.MinValue;

    // CPU 占用采样（GetSystemTimes 两次采样算%）
    private ulong _prevIdle;
    private ulong _prevTotal;
    private bool _cpuInit;
    private readonly DispatcherTimer _systemTimer;
    private readonly DispatcherTimer _infoTimer;

    // 一言：全部从「一言」API 实时获取，不保留本地语录库

    public HomeViewModel(IDshService dsh, IStatusMonitor monitor, ILogService log, ISettingsService settings, INavigationService nav)
    {
        _dsh = dsh;
        _monitor = monitor;
        _log = log;
        _settings = settings;
        _nav = nav;
        _ui = Dispatcher.CurrentDispatcher;
        Export = new SessionExportViewModel(settings);
        SessionTree = new SessionTreeViewModel(settings);

        _monitor.Port = Port;
        _monitor.Start();
        _monitor.StatusChanged += OnStatusChanged;
        _dsh.Exited += OnDshExited;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();

        // 独立系统计时器：每 2s 刷新 CPU/内存（不受 DSH 状态影响，接管模式也实时）
        _systemTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _systemTimer.Tick += (_, _) => { SampleCpu(); RefreshMemory(); };
        _systemTimer.Start();

        // 信息计时器：每 5s 刷模型/会话/工作区（替代手动刷新按钮）
        _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _infoTimer.Tick += (_, _) =>
        {
            if (State != DshState.Running) return;
            _ = RefreshOverviewAsync();
            // 会话树/导出只在进入运行态时刷一次；若当时为空（宿主数据未就绪）会卡在空态 → 定期兜底重试
            if (SessionTree.Workspaces.Count == 0) _ = SessionTree.RefreshAsync();
            if (Export.Sessions.Count == 0) _ = Export.RefreshAsync();
        };
        _infoTimer.Start();

        // 审批通知桥（常驻壳独有）：DSH 后台请求审批 → 托盘通知 → 点击打开嵌入界面
        AppServices.Approval.ApprovalRequested += OnApprovalRequested;

        RefreshMemory(); // 首页内存占用
        SampleCpu();      // 首页 CPU 采样（首次建立基准）
        RandomQuote();        // 一言

        _ = DetectRunningAsync(); // 打开启动器时若 DSH 已在运行 → 直接接管
    }

    [ObservableProperty]
    private DshState _state = DshState.NotRunning;

    /// <summary>当前端口（起来自设置）。</summary>
    public int Port => _settings.Port;

    /// <summary>会话导出卡：选会话导出 Markdown。</summary>
    public SessionExportViewModel Export { get; }

    /// <summary>工作区会话树看板。</summary>
    public SessionTreeViewModel SessionTree { get; }

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>打开界面时的错误（浏览器/嵌入失败），独立于 State 显示。</summary>
    private string _openError = string.Empty;
    public string OpenError
    {
        get => _openError;
        set
        {
            if (SetProperty(ref _openError, value))
            {
                OnPropertyChanged(nameof(HasOpenError));
            }
        }
    }
    public bool HasOpenError => !string.IsNullOrEmpty(OpenError);

    [ObservableProperty]
    private string _uptime = "--";

    [ObservableProperty]
    private string _overviewText = "";

    /// <summary>是否有 DSH 运行概览（官方 API 只读）。</summary>
    public bool HasOverview => !string.IsNullOrEmpty(OverviewText);

    // ---------- 统计卡片（Napcat 风） ----------

    [ObservableProperty]
    private int _sessionCount;
    partial void OnSessionCountChanged(int value)
    {
        OnPropertyChanged(nameof(SessionCountLabel));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(StatsBrush));
    }

    [ObservableProperty]
    private int _workspaceCount;
    partial void OnWorkspaceCountChanged(int value)
    {
        OnPropertyChanged(nameof(WorkspaceCountLabel));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(StatsBrush));
    }

    // ---------- 系统信息 + 内存/磁盘（首页 Napcat 布局数据源） ----------

    [ObservableProperty]
    private string _dshVersion = "--";

    [ObservableProperty]
    private string _dshModel = "--";

    [ObservableProperty]
    private double _memoryPercent;
    partial void OnMemoryPercentChanged(double value) => OnPropertyChanged(nameof(MemoryIdlePercent));

    [ObservableProperty]
    private double _cpuPercent;
    partial void OnCpuPercentChanged(double value) => OnPropertyChanged(nameof(CpuIdlePercent));

    [ObservableProperty]
    private double _diskPercent;

    /// <summary>CPU 空闲 %（图例用）。</summary>
    public double CpuIdlePercent => Math.Clamp(100 - CpuPercent, 0, 100);

    /// <summary>内存空闲 %（图例用）。</summary>
    public double MemoryIdlePercent => Math.Clamp(100 - MemoryPercent, 0, 100);

    /// <summary>实时读系统内存使用率 %（环形图数据源）。</summary>
    private void RefreshMemory()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem)) MemoryPercent = mem.dwMemoryLoad;
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

    // ---------- CPU 占用（GetSystemTimes 采样） ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    /// <summary>采集一次 CPU 占用%（两次采样差分）。在 uptime timer 每秒调用。</summary>
    private void SampleCpu()
    {
        try
        {
            GetSystemTimes(out var idle, out var kernel, out var user);
            var total = kernel + user;
            if (!_cpuInit)
            {
                _prevIdle = (ulong)idle;
                _prevTotal = (ulong)total;
                _cpuInit = true;
                return;
            }
            var idleDelta = (ulong)idle - _prevIdle;
            var totalDelta = (ulong)total - _prevTotal;
            _prevIdle = (ulong)idle;
            _prevTotal = (ulong)total;
            if (totalDelta > 0)
                CpuPercent = Math.Clamp(100.0 * (1 - (double)idleDelta / totalDelta), 0, 100);
        }
        catch { }
    }

    // ---------- 一言（AI/CS 趣味语录） ----------

    [ObservableProperty]
    private string _quote = string.Empty;

    [ObservableProperty]
    private string _quoteAuthor = string.Empty;

    [ObservableProperty]
    private bool _isQuoteLoading;



    [RelayCommand]
    private void RandomQuote() => _ = FetchQuoteAsync();

    /// <summary>联网取「一言」语录（hitokoto API），失败时给一句友好占位。</summary>
    private async Task FetchQuoteAsync()
    {
        IsQuoteLoading = true;
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var quoteUrl = string.IsNullOrWhiteSpace(AppServices.Settings.QuoteApiUrl)
                ? "https://international.v1.hitokoto.cn/?encode=json&max_length=64"
                : AppServices.Settings.QuoteApiUrl!;
            var json = await hc.GetStringAsync(quoteUrl);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("hitokoto", out var h) ? h.GetString() : null;
            if (!string.IsNullOrEmpty(text))
            {
                var from = doc.RootElement.TryGetProperty("from", out var fr) ? fr.GetString() : null;
                var who = doc.RootElement.TryGetProperty("from_who", out var fw) ? fw.GetString() : null;
                var author = string.IsNullOrEmpty(who)
                    ? (string.IsNullOrEmpty(from) ? "一言" : from)
                    : (string.IsNullOrEmpty(from) ? who : $"{who} · {from}");
                Quote = text;
                QuoteAuthor = author;
                return;
            }
        }
        catch { }
        finally { IsQuoteLoading = false; }
        Quote = "（网络暂不可用，稍后再试）";
        QuoteAuthor = "一言";
    }

    [RelayCommand]
    private void CopyQuote()
    {
        try { System.Windows.Clipboard.SetText($"{Quote} —— {QuoteAuthor}"); } catch { }
    }

    public string SessionCountLabel => SessionCount == 1 ? "1 个会话" : $"{SessionCount} 个会话";
    public string WorkspaceCountLabel => WorkspaceCount == 1 ? "1 个工作区" : $"{WorkspaceCount} 个工作区";

    /// <summary>已归档会话数（可点击查看归档，对齐 Codex/GPT）。</summary>
    [ObservableProperty]
    private int _archivedCount;

    /// <summary>已归档会话列表（供归档入口弹窗）。</summary>
    public List<DshSession> ArchivedSessions { get; private set; } = new();

    [RelayCommand]
    private void ShowArchived()
    {
        if (ArchivedSessions.Count == 0)
        {
            MessageBox.Show("没有已归档会话。", "归档会话", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sb = new System.Text.StringBuilder();
        foreach (var s in ArchivedSessions.Take(30))
        {
            var title = string.IsNullOrEmpty(s.Title) ? "(无标题)" : s.Title;
            if (title.Length > 40) title = title[..40] + "…";
            sb.AppendLine($"· {title}");
        }
        if (ArchivedSessions.Count > 30) sb.Append($"… 等 {ArchivedSessions.Count} 个归档会话");
        MessageBox.Show(sb.ToString().TrimEnd(), $"归档会话（{ArchivedSessions.Count}）", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>是否显示统计卡片（DSH 运行时才有数据）。</summary>
    public bool HasStats => SessionCount > 0 || WorkspaceCount > 0;

    /// <summary>统计卡片背景色：有数据时绿色，否则灰色。</summary>
    public Brush StatsBrush => HasStats ? StatusBrushes.Green : StatusBrushes.Gray;

    // ---------- 派生 UI 属性 ----------

    public string BigButtonText => _isStopping ? "正在停止…" : State switch
    {
        DshState.NotRunning => "一键启动 DeepSeek Harness",
        DshState.Starting => "启动中…",
        DshState.Running => "打开 DeepSeek Harness 界面",
        DshState.Error => "异常 — 前往诊断",
        _ => "一键启动 DeepSeek Harness",
    };

    public Brush BigButtonBrush => _isStopping ? StatusBrushes.Gray : State switch
    {
        DshState.NotRunning => StatusBrushes.Blue,
        DshState.Starting => StatusBrushes.Gray,
        DshState.Running => StatusBrushes.Green,
        DshState.Error => StatusBrushes.Red,
        _ => StatusBrushes.Blue,
    };

    public bool IsBigButtonEnabled => !_isStopping && State != DshState.Starting;

    // 统一：不管谁启动的 DSH，只要在跑就能停止/重启（都走确认框）
    private bool DshUp => _dsh.IsRunning || PortProbe.IsListening(Port);
    public bool CanStop => !_isStopping && DshUp;
    public bool CanRestart => !_isStopping && DshUp;

    public bool CanOpen => State == DshState.Running;

    /// <summary>是否处于「接管」模式（DSH 由外部/先前已启动，本启动器不持有进程）。</summary>
    public bool IsAdopted => _adopted;

    public string AdoptedDetail => _adoptedHost is { } h
        ? $"接管运行中 · 版本 {h.Version} · 模型 {h.Provider}/{h.Model} · home {h.Home}"
        : "接管运行中（外部启动）";

    public string StatusText => _isStopping ? "正在停止" : State switch
    {
        DshState.NotRunning => "未运行",
        DshState.Starting => "启动中",
        DshState.Running => "已运行",
        DshState.Error => "异常",
        _ => "未知",
    };

    public Brush StatusBrush => BigButtonBrush;

    public string StatusDetail => State switch
    {
        DshState.NotRunning => "DeepSeek Harness 尚未启动",
        DshState.Starting => "正在启动 DeepSeek Harness…",
        DshState.Running => "可访问",
        DshState.Error => "请前往诊断页排查，或查看日志",
        _ => "",
    };

    public string InfoLine
    {
        get
        {
            if (_adopted && _adoptedHost is { } h)
            {
                var a = new List<string> { $"端口 {Port}" };
                if (h.Version.Length > 0) a.Add($"版本 {h.Version}");
                if (!string.IsNullOrEmpty(h.Provider) && !string.IsNullOrEmpty(h.Model)) a.Add($"模型 {h.Provider}/{h.Model}");
                if (h.Home.Length > 0) a.Add($"home {h.Home}");
                return string.Join("  ·  ", a);
            }
            var parts = new List<string>();
            if (_dsh.Pid is int p) parts.Add($"PID {p}");
            parts.Add($"端口 {Port}");
            if (_dsh.StartedAt is DateTime s) parts.Add($"时长 {Uptime}");
            if (!string.IsNullOrWhiteSpace(_dsh.SafeCommandLine)) parts.Add($"命令 {_dsh.SafeCommandLine}");
            return string.Join("  ·  ", parts);
        }
    }

    public bool HasError => State == DshState.Error && !string.IsNullOrEmpty(ErrorMessage);

    partial void OnStateChanged(DshState value)
    {
        NotifyDerived();
        if (value == DshState.Running && !_readyNotified)
        {
            _readyNotified = true;
            Notifier.ShowDshReady(Port);
        }
        // 审批监控生命周期：DSH 运行→连接 events.mux，否则断开
        if (value == DshState.Running) AppServices.Approval.Start(Port);
        else AppServices.Approval.Stop();
        // 会话导出/会话树列表：进入运行态时刷新
        if (value == DshState.Running)
        {
            _ = Export.RefreshAsync();
            _ = SessionTree.RefreshAsync();
            _autoRestartCount = 0; // 恢复正常运行，重置守护计数
        }
    }

    private void OnApprovalRequested(object? sender, ApprovalRequest req)
    {
        _ui.InvokeAsync(() =>
        {
            Notifier.ShowApproval(req.ToolName, req.Reason, () =>
            {
                // 点击托盘通知 → 启动器审批确认框（允许/拒绝）→ 直接应答官方 /api/respond
                _ui.Invoke(() =>
                {
                    var w = System.Windows.Application.Current.MainWindow;
                    if (w is not null)
                    {
                        w.Show();
                        if (w.WindowState == System.Windows.WindowState.Minimized)
                            w.WindowState = System.Windows.WindowState.Normal;
                        w.Activate();
                    }
                    var reasonTxt = string.IsNullOrEmpty(req.Reason) ? "" : $"\n原因：{req.Reason}";
                    var res = MessageBox.Show($"DeepSeek Harness 请求审批：{req.ToolName}{reasonTxt}\n\n是否允许？",
                        "DeepSeek Harness 审批", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (res == MessageBoxResult.Yes)
                        _ = RespondApprovalAsync(req, "allowed-once");
                    else if (res == MessageBoxResult.No)
                        _ = RespondApprovalAsync(req, "rejected");
                    // Cancel = 稍后处理，不应答
                });
            });
        });
    }

    /// <summary>应答审批（桌面审批中心）：调用官方 /api/respond。仅由用户在弹窗触发。</summary>
    private async Task RespondApprovalAsync(ApprovalRequest req, string outcome)
    {
        try
        {
            var ok = await new DshApiClient(Port).RespondApprovalAsync(req.RpcId, req.SessionId, req.ApprovalId, outcome);
            _log.Append(ok
                ? $"[启动器] 已{(outcome == "allowed-once" ? "允许" : "拒绝")}审批（{req.ToolName}）。"
                : $"[启动器] 审批应答失败（{req.ToolName}）。", ok ? LogSource.Stdout : LogSource.Stderr);
        }
        catch (Exception ex)
        {
            _log.Append($"[启动器] 审批应答异常：{ex.Message}", LogSource.Stderr);
        }
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(BigButtonText));
        OnPropertyChanged(nameof(BigButtonBrush));
        OnPropertyChanged(nameof(IsBigButtonEnabled));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(IsAdopted));
        OnPropertyChanged(nameof(AdoptedDetail));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(InfoLine));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasOverview));
        OnPropertyChanged(nameof(SessionCountLabel));
        OnPropertyChanged(nameof(WorkspaceCountLabel));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(StatsBrush));
    }

    // ---------- DSH 运行概览（官方 API 只读） ----------

    [RelayCommand]
    private Task RefreshOverview() => RefreshOverviewAsync();

    public async Task RefreshOverviewAsync()
    {
        try
        {
            var api = new DshApiClient(Port);
            var host = await api.DescribeHostAsync();
            var sessions = await api.ListSessionsAsync();
            var workspaces = await api.ListWorkspacesAsync();
            var archivedId = (await api.GetArchivedSessionIdsAsync()).ToHashSet();
            // 只统计真实会话：排除子代理派生、空白、归档（与会话树一致，避免 10 个子代理撑数）
            var visible = sessions.Where(s => !s.Blank && !s.IsSubagent && !archivedId.Contains(s.SessionId)).ToList();
            var sb = new System.Text.StringBuilder();
            if (host is not null)
            {
                DshVersion = DshApiClient.GetInstalledDshVersion() ?? host.Version;
                DshModel = string.IsNullOrEmpty(host.Provider) ? (host.Model ?? "--") : $"{host.Provider}/{host.Model}";
                sb.Append($"模型  {host.Provider}/{host.Model}  ·  附加会话 {host.AttachedSessions}");
                if (host.CanOpenPath) sb.Append("  ·  可打开路径");
                sb.AppendLine();
            }
            var running = visible.Where(s => s.Running).ToList();
            sb.AppendLine($"会话  共 {visible.Count} 个（运行中 {running.Count}）");
            // 只列运行中的会话（归档/旧会话在 DSH UI 里没有查看入口，不列，避免噪音）
            foreach (var s in running.Take(4))
            {
                var title = string.IsNullOrEmpty(s.Title) ? "(无标题)" : s.Title;
                if (title.Length > 40) title = title[..40] + "…";
                sb.AppendLine($"  · ▶ {title}  [{s.Cwd}]");
            }
            if (running.Count > 4) sb.AppendLine($"  … 等 {running.Count - 4} 个运行中");
            sb.AppendLine($"工作区  {workspaces.Count} 个");
            foreach (var w in workspaces.Take(3)) sb.AppendLine($"  · {w.Title} @ {w.Path}");
            OverviewText = sb.ToString();
            OnPropertyChanged(nameof(HasOverview));

            // 统计卡片：会话 = 运行中的真实会话（排除子代理等）
            SessionCount = visible.Count(s => s.Running);
            WorkspaceCount = workspaces.Count;
            FileLog.Write($"RefreshOverview: sessions={sessions.Count} running={SessionCount} State={State}");
        }
        catch
        {
            OverviewText = "";
            OnPropertyChanged(nameof(HasOverview));
        }
    }

    // ---------- 运行中 DSH 探测接管 ----------

    private async Task DetectRunningAsync()
    {
        try
        {
            var open = PortProbe.IsListening(Port);
            FileLog.Write($"DetectRunningAsync: portOpen={open} Port={Port}");
            if (!open) return;
            var host = await new DshApiClient(Port).DescribeHostAsync();
            FileLog.Write($"DetectRunningAsync: host.describe => {(host is null ? "null（非 DSH？）" : host.Version)}");
            if (host is null) return;
            _adopted = true;
            _adoptedHost = host;
            _log.Append($"[启动器] 检测到 DSH 已在运行（版本 {host.Version}），已接管。", LogSource.Stdout);
            State = DshState.Running;
            NotifyDerived();
            _ = RefreshOverviewAsync();
        }
        catch
        {
            // 探测失败静默（视为未运行）
        }
    }

    // ---------- 命令 ----------

    [RelayCommand]
    private void BigButton()
    {
        switch (State)
        {
            case DshState.NotRunning:
                _ = StartAsync();
                break;
            case DshState.Running:
                OpenWeb();
                break;
            case DshState.Error:
                _nav.NavigateTo<DoctorViewModel>();
                break;
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        FileLog.Write($"StartAsync entered: State={State} _isStopping={_isStopping} Port={Port}");
        if (_isStopping || State is DshState.Starting or DshState.Running) return;
        ErrorMessage = string.Empty;
        OpenError = string.Empty;
        _readyNotified = false;

        // 端口已开放 → 识别是否为 DSH（官方 host.describe），是则接管、否则报端口被占
        if (PortProbe.IsListening(Port))
        {
            var host = await new DshApiClient(Port).DescribeHostAsync();
            if (host is not null)
            {
                _adopted = true;
                _adoptedHost = host;
                _log.Append($"[启动器] 检测到 DSH 已在运行（版本 {host.Version}），已接管。", LogSource.Stdout);
                State = DshState.Running;
                NotifyDerived();
                return;
            }
            ErrorMessage = $"端口 {Port} 已被其他程序占用，请先在设置页更换端口（启动器不会自动杀掉占用者）。";
            State = DshState.Error;
            return;
        }

        _adopted = false;
        _adoptedHost = null;
        State = DshState.Starting;
        _uptimeTimer.Start();
        try
        {
            FileLog.Write($"StartAsync: calling DshService.StartAsync(Port={Port})");
            await _dsh.StartAsync(Port, AppServices.Settings.ExtraArgs, AppServices.Settings.PatchFile);
            FileLog.Write($"StartAsync: DshService started, IsRunning={_dsh.IsRunning} Pid={_dsh.Pid}");
            _monitor.Port = Port;
            _log.Append($"[启动器] 已启动 DSH（PID {_dsh.Pid}），等待端口 {Port} 就绪…", LogSource.Stdout);
            _ = WaitUntilRunningAsync();
        }
        catch (Exception ex)
        {
            FileLog.Write($"StartAsync FAILED: {ex.GetType().Name}: {ex.Message}");
            _log.Append($"[启动器] 启动失败：{ex.Message}", LogSource.Stderr);
            ErrorMessage = ex.Message;
            State = DshState.Error;
            _uptimeTimer.Stop();
        }
    }

    private async Task WaitUntilRunningAsync()
    {
        // 首启可能拉 npx 依赖、构建 native 库，给到 120s（稳定性）
        for (var i = 0; i < 240; i++)
        {
            if (State != DshState.Starting) return;
            bool open;
            try { open = await _monitor.CheckAsync(); } catch { open = false; }
            if (open)
            {
                _ = _ui.InvokeAsync(() =>
                {
                    if (State == DshState.Starting) State = DshState.Running;
                    _log.Append("[启动器] DSH 已就绪。", LogSource.Stdout);
                    _ = RefreshOverviewAsync();
                });
                return;
            }
            await Task.Delay(500);
        }
        _ = _ui.InvokeAsync(() =>
        {
            if (State == DshState.Starting)
            {
                ErrorMessage = "等待端口开放超时（120s），可能首次启动仍在拉依赖，请查看日志或重试。";
                State = DshState.Error;
            }
        });
    }

    /// <summary>统一停止确认（自举/接管都弹，文案按模式区分）。</summary>
    private bool ConfirmStop()
    {
        var text = "确认要停止 DeepSeek Harness 吗？\n\n注意：若该 DeepSeek Harness 正被其他程序使用，停止会中断它。";
        return MessageBox.Show(text, "停止 DeepSeek Harness", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (_isStopping || !CanStop) return;
        // 统一确认：自举/接管一致
        if (!ConfirmStop()) return;
        _isStopping = true;
        NotifyDerived();
        try
        {
            if (IsAdopted)
            {
                if (!await StopAdoptedCoreAsync()) return; // 失败，状态不变
            }
            else
            {
                await _dsh.StopAsync();
            }
            _readyNotified = false;
            _adopted = false;
            _adoptedHost = null;
            State = DshState.NotRunning;
            _uptimeTimer.Stop();
            Uptime = "--";
            OverviewText = "";
            OnPropertyChanged(nameof(HasOverview));
        }
        finally
        {
            _isStopping = false;
            NotifyDerived();
        }
    }

    /// <summary>停止外部 DSH 核心（确认已在 StopAsync/RestartAsync 统一弹过）：host.describe 复核 → netstat 找 PID → taskkill 整树。</summary>
    private async Task<bool> StopAdoptedCoreAsync()
    {
        var api = new DshApiClient(Port);
        if (await api.DescribeHostAsync() is null)
        {
            _log.Append($"[启动器] 停止：端口 {Port} 已不再响应 DSH。", LogSource.Stderr);
            _adopted = false;
            _adoptedHost = null;
            return true; // 端口已无 DSH，视为已停止
        }
        var ok = await _dsh.StopExternalAsync(Port);
        _log.Append(ok
            ? $"[启动器] 已停止外部 DSH（端口 {Port}）。"
            : $"[启动器] 停止外部 DSH 失败（端口 {Port}）。", ok ? LogSource.Stdout : LogSource.Stderr);
        if (!ok) ErrorMessage = "停止外部 DeepSeek Harness 失败，请查看日志。";
        return ok;
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (_isStopping) return;
        // 统一确认：自举/接管一致
        var confirmText = "确认要重启 DeepSeek Harness 吗？\n\n注意：若该 DeepSeek Harness 正被其他程序使用，重启会中断它。";
        if (MessageBox.Show(confirmText, "重启 DeepSeek Harness", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        // 接管模式：先停外部 DSH，再改为由本启动器 spawn
        if (IsAdopted)
        {
            if (!await StopAdoptedCoreAsync()) return;
            _adopted = false;
            _adoptedHost = null;
        }
        ErrorMessage = string.Empty;
        State = DshState.Starting;
        _uptimeTimer.Start();
        try
        {
            await _dsh.RestartAsync(Port, AppServices.Settings.ExtraArgs, AppServices.Settings.PatchFile);
            _monitor.Port = Port;
            _log.Append("[启动器] 已重启 DSH。", LogSource.Stdout);
            _ = WaitUntilRunningAsync();
        }
        catch (Exception ex)
        {
            _log.Append($"[启动器] 重启失败：{ex.Message}", LogSource.Stderr);
            ErrorMessage = ex.Message;
            State = DshState.Error;
            _uptimeTimer.Stop();
        }
    }

    private void OpenWeb()
    {
        if (State != DshState.Running) return;
        OpenInterface();
    }

    /// <summary>打开 DSH 界面：按设置用系统浏览器，或 WebView2 嵌入（用户偏好）。</summary>
    public void OpenInterface()
    {
        if (State != DshState.Running) return;
        if (_settings.OpenInBrowser)
        {
            _ = OpenBrowserAsync();
        }
        else
        {
            _nav.ShowEmbed(); // WebView2 嵌入页
        }
    }

    /// <summary>打开指定会话：dsh web 深链 ?session=&lt;id&gt; 会跳到该会话（会话列表点进去）。</summary>
    [RelayCommand]
    public void OpenSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        try
        {
            var url = $"http://127.0.0.1:{Port}/?session={Uri.EscapeDataString(sessionId)}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            OpenError = "打开会话失败：" + ex.Message;
        }
    }

    /// <summary>通过 DshApiClient 获取接口 URL，走系统浏览器打开（不硬编码协议/路径）。</summary>
    private async Task OpenBrowserAsync()
    {
        var api = new DshApiClient(Port);
        var url = await api.GetInterfaceUrlAsync();
        if (url is null)
        {
            OpenError = $"DeepSeek Harness 接口不可达（端口 {Port}），无法打开浏览器。";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            OpenError = $"无法打开浏览器：{ex.Message}";
        }
    }

    // ---------- 事件 ----------

    private void OnStatusChanged(object? sender, bool open)
    {
        _ui.InvokeAsync(() =>
        {
            if (open && State == DshState.Starting) State = DshState.Running;
            if (!open && State == DshState.Running && !_dsh.IsRunning) State = DshState.NotRunning;
        });
    }

    /// <summary>守护模式：DSH 异常退出后延时自动重启，带次数护栏（60s 窗口内最多 3 次）。</summary>
    private async Task TryAutoRestartAsync(int code)
    {
        var now = DateTime.Now;
        if ((now - _lastAutoRestart).TotalSeconds > 60) _autoRestartCount = 0;
        _lastAutoRestart = now;

        if (_autoRestartCount >= 3)
        {
            ErrorMessage = $"DeepSeek Harness 进程意外退出（退出码 {code}）。已自动重启 {_autoRestartCount} 次仍失败，已停止尝试，请查看日志。";
            State = DshState.Error;
            return;
        }

        _autoRestartCount++;
        ErrorMessage = $"DeepSeek Harness 进程意外退出（退出码 {code}）。第 {_autoRestartCount} 次自动重启…";
        await Task.Delay(3000);

        // 延迟后可能已恢复（如仍在启动/端口已开）→ 不再重复拉起
        if (_dsh.IsRunning || PortProbe.IsListening(Port) || _isStopping)
        {
            State = _dsh.IsRunning || PortProbe.IsListening(Port) ? DshState.Running : DshState.NotRunning;
            return;
        }
        if (_adopted) return;

        State = DshState.NotRunning; // 让 StartAsync 通过其 Starting/Running 守卫
        FileLog.Write($"AutoRestart: attempt {_autoRestartCount} -> StartCommand.Execute");
        StartCommand.Execute(null);
    }

    private void OnDshExited(object? sender, int code)
    {
        _ui.InvokeAsync(() =>
        {
            if (_dsh.WasStopRequested || _isStopping)
            {
                if (State != DshState.Starting) State = DshState.NotRunning;
                return;
            }
            // 非主动停止的退出 → 异常；守护模式开启则自动重启
            ErrorMessage = $"DeepSeek Harness 进程意外退出（退出码 {code}）。请查看日志。";
            State = DshState.Error;
            _uptimeTimer.Stop();
            if (_settings.AutoRestartOnCrash && !_adopted) _ = TryAutoRestartAsync(code);
        });
    }

    private void UpdateUptime()
    {
        if (_dsh.StartedAt is DateTime s && _dsh.IsRunning)
        {
            var t = DateTime.Now - s;
            Uptime = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
                : t.TotalMinutes >= 1
                    ? $"{t.Minutes}m {t.Seconds}s"
                    : $"{t.Seconds}s";
            OnPropertyChanged(nameof(InfoLine));
        }
    }

}
