using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DshLauncher.Core;

/// <summary>
/// 审批通知桥（启动器常驻壳独有能力）：连官方 /api/events.mux WebSocket，
/// 监听 approval/requested 帧，DSH 在后台请求审批时推送 Windows 托盘通知。
/// 生命周期由 HomeViewModel 驱动（DSH 运行→Start，停止→Stop）。断线自动退避重连。
/// 只读订阅；审批应答（api/respond）交给嵌入的官方 UI 处理，本类不做写操作。
/// </summary>
public sealed class ApprovalMonitor : IDisposable
{
    private static readonly TimeSpan ReconnectBaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _sessionCts;
    private readonly object _gate = new();
    private Task? _loop;
    private int _reconnectAttempt;

    /// <summary>DSH 请求审批（toolName/reason 已脱敏，不含参数值）。</summary>
    public event EventHandler<ApprovalRequest>? ApprovalRequested;

    public bool IsRunning
    {
        get { lock (_gate) return _loop is { IsCompleted: false }; }
    }

    /// <summary>启动监控（连接 DSH 的 events.mux）。重复调用忽略。</summary>
    public void Start(int port)
    {
        lock (_gate)
        {
            if (_loop is { IsCompleted: false }) return;
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _loop = Task.Run(() => LoopAsync(port, _sessionCts.Token));
        }
    }

    /// <summary>停止监控（断开连接）。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;
            _loop = null;
        }
    }

    private async Task LoopAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                var uri = new Uri($"ws://127.0.0.1:{port}/api/events.mux");
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                await ws.ConnectAsync(uri, connectCts.Token);
                _reconnectAttempt = 0;

                var buffer = new byte[16 * 1024];
                var frame = new StringBuilder();
                while (!ct.IsCancellationRequested)
                {
                    var seg = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await ws.ReceiveAsync(seg, ct);
                    }
                    catch (WebSocketException) when (!ct.IsCancellationRequested)
                    {
                        break; // 连接被关 → 重连
                    }
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }
                    TryParseFrame(frame.ToString());
                    frame.Clear();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break; // 主动停止
            }
            catch
            {
                // 连接失败/异常 → 退避重连
            }
            finally
            {
                try { ws?.Dispose(); } catch { }
            }

            if (ct.IsCancellationRequested) break;
            _reconnectAttempt++;
            var delay = TimeSpan.FromMilliseconds(
                Math.Min(ReconnectBaseDelay.TotalMilliseconds * (1 << Math.Min(_reconnectAttempt, 5)),
                         ReconnectMaxDelay.TotalMilliseconds));
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void TryParseFrame(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var m)) return;
            var method = m.GetString();
            if (method != "approval/requested") return;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;

            // 外层 ServerRequest 的 rpcId（应答 /api/respond 需回显）+ approvalId 取帧内
            var rpcId = Get(root, "rpcId");
            var sessionId = Get(payload, "sessionId");
            var approvalId = Get(payload, "approvalId");
            var toolName = Get(payload, "toolName");
            var reason = Get(payload, "reason");
            if (approvalId.Length == 0 || sessionId.Length == 0) return;
            ApprovalRequested?.Invoke(this, new ApprovalRequest(sessionId, approvalId, toolName, reason, rpcId));
        }
        catch
        {
            // 非 JSON / 解析失败忽略
        }
    }

    private static string Get(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";

    public void Dispose()
    {
        Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

/// <summary>一次审批请求（已脱敏：toolName/reason 不含调用参数值；RpcId 用于 /api/respond 回显）。</summary>
public sealed record ApprovalRequest(string SessionId, string ApprovalId, string ToolName, string Reason, string RpcId = "");
