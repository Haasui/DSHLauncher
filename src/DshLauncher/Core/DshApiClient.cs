using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace DshLauncher.Core;

/// <summary>
/// 官方 HTTP API 客户端：POST /api/<domain.method>，信封 {type:"client-request", rpcId, method, payload}。
/// 已实测（对本机运行中 DSH）：host.describe / settings.describe 只读可用。
/// 只提供只读方法；写方法（settings.update/mutate 等）待用户验收后再加。
/// </summary>
public sealed class DshApiClient
{
    private readonly HttpClient _http;
    private readonly string _base;

    public DshApiClient(int port = 3080)
    {
        Port = port;
        _base = $"http://127.0.0.1:{port}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>构造时传入的端口（只读，供外部查询）。</summary>
    public int Port { get; }

    /// <summary>完整接口基础 URL。若 DSH 未来改用其他协议/路径，改这一处即可。</summary>
    public string BaseUrl => _base;

    /// <summary>
    /// 校验 DSH 是否可访问 + 返回接口 URL。
    /// 调用 host.describe 确认是 DSH 而非其他进程，成功返回 URL，失败返回 null。
    /// 所有调用方通过此方法获取 URL，不硬编码协议/路径。
    /// </summary>
    public async Task<string?> GetInterfaceUrlAsync(CancellationToken ct = default)
    {
        try
        {
            var host = await DescribeHostAsync(ct);
            return host is null ? null : _base;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>调用一个官方 RPC；成功返回 result.value 的 JsonElement（可空），业务错误抛 DshApiException。</summary>
    public async Task<JsonElement?> CallAsync(string method, object? payload = null, CancellationToken ct = default)
    {
        var envelope = new { type = "client-request", rpcId = Guid.NewGuid().ToString("N"), method, payload = payload ?? new { } };
        using var resp = await _http.PostAsJsonAsync($"{_base}/api/{method}", envelope, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new DshApiException("bad-response", "响应缺少 result 字段");
        if (result.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            return result.TryGetProperty("value", out var value) ? value.Clone() : null;
        var err = result.TryGetProperty("error", out var e) ? e : default;
        var code = err.TryGetProperty("code", out var c) ? c.GetString() : "error";
        var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "调用失败";
        throw new DshApiException(code ?? "error", msg ?? "调用失败");
    }

    /// <summary>host.describe：版本/当前模型/工作目录/home。</summary>
    public async Task<DshHostInfo?> DescribeHostAsync(CancellationToken ct = default)
    {
        try
        {
            var v = await CallAsync("host.describe", ct: ct);
            if (v is not { ValueKind: JsonValueKind.Object } obj) return null;
            return new DshHostInfo(
                Str(obj, "version"),
                Str(obj, "cwd"),
                Str(obj, "provider"),
                Str(obj, "model"),
                Int(obj, "attachedSessions"),
                Str(obj, "home"),
                Bool(obj, "canOpenPath"));
        }
        catch
        {
            return null; // 不可达/非 DSH 时静默
        }
    }

    /// <summary>settings.describe：全部命名空间的实时视图（ns/applies/revision/value/user/base）。</summary>
    public async Task<IReadOnlyList<DshSettingsNamespace>> DescribeSettingsAsync(CancellationToken ct = default)
    {
        var v = await CallAsync("settings.describe", ct: ct);
        var list = new List<DshSettingsNamespace>();
        if (v is null || !v.Value.TryGetProperty("namespaces", out var ns)) return list;
        // writable 在顶层（value.writable），不在命名空间对象上；一次读取传给每个命名空间
        var writable = v.Value.TryGetProperty("writable", out var w) && w.ValueKind == JsonValueKind.True;
        foreach (var n in ns.EnumerateArray())
        {
            list.Add(new DshSettingsNamespace(
                Str(n, "ns"),
                Str(n, "applies"),
                writable,
                Long(n, "revision"),
                Compact(n, "value"),
                Compact(n, "user"),
                Compact(n, "base")));
        }
        return list;
    }

    /// <summary>
    /// settings.mutate：在已存设置分节上施加路径 op（set/unset），带 CAS expectedRevision 防冲突。
    /// 写操作仅由用户显式触发（如主题切换），启动器绝不自动写入 ~/.dsh。
    /// </summary>
    public async Task<bool> MutateSettingsAsync(
        string ns, string op, string[] path, object? value,
        long? expectedRevision = null, CancellationToken ct = default)
    {
        object payload;
        if (expectedRevision is { } rev)
        {
            payload = new
            {
                ns,
                ops = new[] { new { op, path, value = value ?? new { } } },
                expectedRevision = rev,
            };
        }
        else
        {
            payload = new
            {
                ns,
                ops = new[] { new { op, path, value = value ?? new { } } },
            };
        }
        try
        {
            var v = await CallAsync("settings.mutate", payload, ct);
            return v is { ValueKind: JsonValueKind.Object };
        }
        catch (DshApiException)
        {
            throw; // 业务错误（如 settings-conflict / settings-rejected）原样抛给调用方展示
        }
    }

    /// <summary>agent-default-model：读取默认模型的 provider/model/reasoningEffort + revision + 是否可写。</summary>
    public async Task<DshDefaultModel?> GetDefaultModelAsync(CancellationToken ct = default)
    {
        var v = await CallAsync("settings.describe", ct: ct);
        if (v is null) return null;
        var writable = v.Value.TryGetProperty("writable", out var w) && w.ValueKind == JsonValueKind.True;
        if (!v.Value.TryGetProperty("namespaces", out var ns)) return new DshDefaultModel("", "", "", 0, writable);
        foreach (var n in ns.EnumerateArray())
        {
            if (Str(n, "ns") != "agent-default-model") continue;
            long rev = Long(n, "revision");
            string provider = "", modelId = "", reasoning = "";
            if (n.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Object)
            {
                provider = Str(val, "provider");
                modelId = Str(val, "model");
                reasoning = Str(val, "reasoningEffort");
            }
            return new DshDefaultModel(provider, modelId, reasoning, rev, writable);
        }
        return new DshDefaultModel("", "", "", 0, writable);
    }

    /// <summary>agent-default-model：设置默认模型（settings.mutate，CAS expectedRevision）。写操作仅由用户显式触发。</summary>
    public Task<bool> SetDefaultModelAsync(string provider, string model, string reasoningEffort,
        long expectedRevision, CancellationToken ct = default)
        => MutateSettingsAsync("agent-default-model", "set", Array.Empty<string>(),
            new { provider, model, reasoningEffort }, expectedRevision, ct);

    /// <summary>读取 DSH 界面主题（ui-theme preference）：light | dark | system。启动器深/浅色据此联动。</summary>
    public async Task<string?> GetWebThemeAsync(CancellationToken ct = default)
    {
        var v = await CallAsync("settings.describe", ct: ct);
        if (v is null || !v.Value.TryGetProperty("namespaces", out var ns)) return null;
        foreach (var n in ns.EnumerateArray())
        {
            if (Str(n, "ns") != "ui-theme") continue;
            if (n.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Object)
            {
                var pref = Str(val, "preference");
                if (pref is "light" or "dark" or "system") return pref;
            }
        }
        return null;
    }

    /// <summary>
    /// 应答一次审批（桌面审批中心）：POST /api/respond，client-response 信封回显 rpcId。
    /// outcome = allowed-once | rejected。仅由用户在审批弹窗触发，启动器不自动应答。
    /// </summary>
    public async Task<bool> RespondApprovalAsync(string rpcId, string sessionId, string approvalId, string outcome, CancellationToken ct = default)
    {
        var envelope = new
        {
            type = "client-response",
            rpcId,
            result = new
            {
                ok = true,
                value = new { sessionId, approvalId, outcome },
            },
        };
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{_base}/api/respond", envelope, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>session.list：会话列表（id/运行状态/cwd/agentPreset/标题）。</summary>
    public async Task<IReadOnlyList<DshSession>> ListSessionsAsync(CancellationToken ct = default)
    {
        var list = new List<DshSession>();
        try
        {
            var v = await CallAsync("session.list", ct: ct);
            if (v is null || !v.Value.TryGetProperty("items", out var items)) return list;
            foreach (var s in items.EnumerateArray())
            {
                var title = "";
                if (s.TryGetProperty("projections", out var proj)
                    && proj.TryGetProperty("values", out var vals)
                    && vals.TryGetProperty("title", out var t)
                    && t.ValueKind == JsonValueKind.String) title = t.GetString() ?? "";
                list.Add(new DshSession(
                    Str(s, "sessionId"),
                    Bool(s, "running"),
                    Str(s, "cwd"),
                    Str(s, "agentPreset"),
                    title,
                    Bool(s, "blank"),
                    Str(s, "parentSessionId"),
                    Str(s, "origin")));
            }
        }
        catch { }
        return list;
    }

    /// <summary>session.search：跨会话内容搜索（官方，至多 20 个命中，每 snippet ≤240 字符）。</summary>
    public async Task<IReadOnlyList<DshSearchHit>> SearchSessionsAsync(string query, CancellationToken ct = default)
    {
        var list = new List<DshSearchHit>();
        try
        {
            var v = await CallAsync("session.search", new { query }, ct);
            if (v is null || !v.Value.TryGetProperty("items", out var items)) return list;
            foreach (var it in items.EnumerateArray())
            {
                var id = Str(it, "sessionId");
                var snippet = Str(it, "snippet");
                if (id.Length > 0) list.Add(new DshSearchHit(id, snippet));
            }
        }
        catch (DshApiException) { throw; } // 业务错误（如跨会话搜索被禁用）原样抛给上层提示
        catch { }
        return list;
    }

    /// <summary>workspace.list：工作区列表。</summary>
    public async Task<IReadOnlyList<DshWorkspace>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var list = new List<DshWorkspace>();
        try
        {
            var v = await CallAsync("workspace.list", ct: ct);
            if (v is null || !v.Value.TryGetProperty("items", out var items)) return list;
            foreach (var w in items.EnumerateArray())
            {
                var ids = new List<string>();
                if (w.TryGetProperty("sessionIds", out var sids))
                    foreach (var id in sids.EnumerateArray()) if (id.ValueKind == JsonValueKind.String) ids.Add(id.GetString() ?? "");
                list.Add(new DshWorkspace(Str(w, "workspaceId"), Str(w, "path"), Str(w, "title"), ids));
            }
        }
        catch { }
        return list;
    }

    /// <summary>workspace.list 顶层的已归档会话 id 集合（区分活动/归档会话）。</summary>
    public async Task<IReadOnlyList<string>> GetArchivedSessionIdsAsync(CancellationToken ct = default)
    {
        var set = new List<string>();
        try
        {
            var v = await CallAsync("workspace.list", ct: ct);
            if (v is null || !v.Value.TryGetProperty("archivedSessionIds", out var ar)) return set;
            foreach (var id in ar.EnumerateArray())
                if (id.ValueKind == JsonValueKind.String) set.Add(id.GetString() ?? "");
        }
        catch { }
        return set;
    }

    /// <summary>读取本机 npm 全局安装的 dsh 版本（host.describe 上报的是 Harness 宿主
    /// 内部 0.0.1，非用户装的 dsh 包版本），读 package.json 避免每次 spawn 进程。</summary>
    public static string? GetInstalledDshVersion()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@deepseek-ai", "dsh", "package.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "npm", "node_modules", "@deepseek-ai", "dsh", "package.json"),
        };
        foreach (var pj in candidates)
        {
            try
            {
                if (!File.Exists(pj)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(pj));
                if (doc.RootElement.TryGetProperty("version", out var v)) return v.GetString();
            }
            catch { }
        }
        return null;
    }

    private static string Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";

    private static long Long(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt64() : 0;

    private static int Int(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0;

    private static bool Bool(JsonElement o, string name)
        => o.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.True;

    /// <summary>把 value/user/base 压成紧凑 JSON 字符串（截断防 UI 卡顿）。</summary>
    private static string Compact(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var x) || x.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "∅";
        try
        {
            var s = JsonSerializer.Serialize(x);
            return s.Length <= 300 ? s : s[..300] + "…";
        }
        catch { return "∅"; }
    }
}

/// <summary>session.search 单条命中。</summary>
public sealed record DshSearchHit(string SessionId, string Snippet);

/// <summary>host.describe 结果。</summary>
public sealed record DshHostInfo(
    string Version, string Cwd, string? Provider, string? Model,
    int AttachedSessions, string Home, bool CanOpenPath);

/// <summary>会话概览（session.list 只读）。</summary>
public sealed record DshSession(string SessionId, bool Running, string Cwd, string AgentPreset, string Title, bool Blank = false, string ParentSessionId = "", string Origin = "")
{
    /// <summary>子代理派生会话（origin=subagent 或带 parentSessionId），非用户对话，不应出现在会话列表。</summary>
    public bool IsSubagent => Origin == "subagent" || !string.IsNullOrEmpty(ParentSessionId);
}

/// <summary>工作区概览（workspace.list 只读）。</summary>
public sealed record DshWorkspace(string WorkspaceId, string Path, string Title, IReadOnlyList<string> SessionIds);

/// <summary>settings.describe 单命名空间视图。</summary>
public sealed record DshSettingsNamespace(
    string Ns, string Applies, bool Writable, long Revision,
    string ValueJson, string UserJson, string BaseJson);

/// <summary>agent-default-model 读取结果。</summary>
public sealed record DshDefaultModel(string Provider, string Model, string ReasoningEffort, long Revision, bool Writable);