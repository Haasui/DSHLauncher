using System.IO;
using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace DshLauncher.Core;

/// <summary>
/// 会话导出：读取 dsh 本地会话转存 ~/.dsh/sessions/&lt;cwd&gt;/&lt;sessionId&gt;/session.jsonl.zstd，
/// <b>流式解压 + 逐行解析</b>（超大会话可到数 GB），还原为可读 Markdown 写入目标流。
/// 仅读取 dsh 磁盘上的会话数据，不修改任何内容。
/// </summary>
public static class SessionExporter
{
    public sealed record ExportOutcome(bool Ok, string Message);

    /// <summary>在用户 dsh 数据目录下定位某会话的转存文件；找不到返回 null。</summary>
    public static string? LocateTranscript(string sessionId)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dsh", "sessions");
        if (!Directory.Exists(root)) return null;
        try
        {
            foreach (var cwdDir in Directory.EnumerateDirectories(root))
            {
                var f = Path.Combine(cwdDir, sessionId, "session.jsonl.zstd");
                if (File.Exists(f)) return f;
            }
        }
        catch { }
        return null;
    }

    /// <summary>导出会话为 Markdown 到目标流（流式，支持超大会话）。</summary>
    public static ExportOutcome ExportToMarkdown(string sessionId, string? titleHint, Stream dest)
    {
        var file = LocateTranscript(sessionId);
        if (file is null) return new(false, "找不到该会话的转存文件。");
        try
        {
            var st = new ExportState { Title = titleHint ?? "" };
            var input = File.ReadAllBytes(file);
            using var d = new Decompressor();
            var chunk = new byte[1 << 16];
            var chars = new char[1 << 16];
            var line = new StringBuilder();
            var dec = Encoding.UTF8.GetDecoder(); // 跨块保留多字节序列，避免中文乱码
            int off = 0;
            while (true)
            {
                int consumed, written;
                d.UnwrapStream(input.AsSpan(off), chunk, out consumed, out written);
                off += consumed;
                if (written == 0 && consumed == 0) break;
                dec.Convert(chunk.AsSpan(0, written), chars, flush: false, out _, out int charsUsed, out _);
                AppendDecoded(chars.AsSpan(0, charsUsed), line, st);
            }
            dec.Convert(ReadOnlySpan<byte>.Empty, chars, flush: true, out _, out int flushed, out _);
            AppendDecoded(chars.AsSpan(0, flushed), line, st);
            if (line.Length > 0) ProcessLine(line.ToString(), st);

            using var w = new StreamWriter(dest, new UTF8Encoding(false), 1 << 16);
            w.WriteLine($"# {Title(st.Title)}");
            w.WriteLine();
            w.WriteLine($"> 会话 {st.Id}  ·  cwd {st.Cwd}  ·  {st.Created}");
            w.WriteLine();
            foreach (var u in st.Users) { w.Write(u); w.WriteLine(); }
            foreach (var key in st.StepOrder)
            {
                if (!st.Steps.TryGetValue(key, out var s)) continue;
                RenderStep(w, s);
            }
            w.Flush();
            return new(true, "已导出");
        }
        catch (Exception ex)
        {
            return new(false, "导出失败：" + ex.Message);
        }
    }

    private static void AppendDecoded(ReadOnlySpan<char> chars, StringBuilder line, ExportState st)
    {
        foreach (var c in chars)
        {
            if (c == '\n') { ProcessLine(line.ToString(), st); line.Clear(); }
            else if (c != '\r') line.Append(c);
        }
    }

    private static void ProcessLine(string jsonLine, ExportState st)
    {
        if (string.IsNullOrWhiteSpace(jsonLine)) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonLine); }
        catch { return; }
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var tp) ? tp.GetString() : "";
        var data = root.TryGetProperty("data", out var d) ? d : default;
        switch (type)
        {
            case "session":
                st.Id = Str(root, "id", st.Id);
                st.Cwd = Str(root, "cwd", st.Cwd);
                st.Created = Str(root, "createdAt", st.Created);
                break;
            case "session/title":
                st.Title = Str(data, "title", st.Title);
                break;
            case "user/message":
                st.Users.Add(RenderUser(data));
                break;
            case "assistant/message":
            {
                int turn = Int(data, "turn", 0);
                int step = Int(data, "step", 0);
                var s = PopulateStep(data);
                if (s is not null)
                {
                    if (!st.StepSeen.Contains((turn, step))) { st.StepOrder.Add((turn, step)); st.StepSeen.Add((turn, step)); }
                    st.Steps[(turn, step)] = s;
                }
                break;
            }
            case "tool/result":
            {
                int turn = Int(data, "turn", 0);
                int step = Int(data, "step", 0);
                string callId = "";
                bool isError = data.TryGetProperty("error", out _);
                string resultText = "";
                if (data.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c2))
                {
                    foreach (var part in c2.EnumerateArray())
                    {
                        if (part.TryGetProperty("toolCallId", out var tc)) callId = tc.GetString() ?? "";
                        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("content", out var inner))
                            resultText += CollectText(inner);
                    }
                }
                if (st.Steps.TryGetValue((turn, step), out var s))
                    s.Results.Add((callId, isError, resultText));
                break;
            }
        }
        doc.Dispose();
    }

    private static void RenderStep(TextWriter w, Step s)
    {
        if (!string.IsNullOrWhiteSpace(s.Reasoning))
        {
            w.WriteLine("<details><summary>💭 思考</summary>");
            w.WriteLine();
            w.WriteLine(s.Reasoning.Trim());
            w.WriteLine();
            w.WriteLine("</details>");
            w.WriteLine();
        }
        if (!string.IsNullOrWhiteSpace(s.Text))
        {
            w.WriteLine("**助手**");
            w.WriteLine();
            w.WriteLine(s.Text.Trim());
            w.WriteLine();
        }
        foreach (var (callId, name, args) in s.ToolCalls)
        {
            w.WriteLine($"<details><summary>🛠 工具 · {name}</summary>");
            w.WriteLine();
            w.WriteLine("<pre>");
            w.WriteLine(Escape(args.Trim()));
            w.WriteLine("</pre>");
            w.WriteLine();
            var res = s.Results.FirstOrDefault(r => r.Item1 == callId);
            if (res != default)
            {
                w.WriteLine(res.Item2 ? "> ⚠️ 执行出错" : "> ✅ 执行成功");
                if (!string.IsNullOrWhiteSpace(res.Item3))
                {
                    w.WriteLine();
                    var t = res.Item3.Trim();
                    w.WriteLine(t.Length > 1500 ? t[..1500] + "…" : t);
                }
            }
            w.WriteLine("</details>");
            w.WriteLine();
        }
    }

    private static Step? PopulateStep(JsonElement data)
    {
        if (!data.TryGetProperty("message", out var msg)) return null;
        var st = new Step();
        if (msg.TryGetProperty("content", out var c))
        {
            foreach (var part in c.EnumerateArray())
            {
                var pt = part.TryGetProperty("type", out var t2) ? t2.GetString() : "";
                if (pt == "text" && part.TryGetProperty("text", out var tx))
                    st.Text = st.Text + "\n\n" + tx.GetString();
                else if (pt == "reasoning" && part.TryGetProperty("text", out var rt))
                    st.Reasoning = st.Reasoning + "\n\n" + rt.GetString();
                else if (pt == "tool-call")
                {
                    var name = part.TryGetProperty("name", out var nn) ? nn.GetString() ?? "tool" : "tool";
                    var callId = part.TryGetProperty("id", out var ci) ? ci.GetString() ?? "" : "";
                    var args = part.TryGetProperty("arguments", out var aa) ? aa.GetString() ?? "" : "";
                    st.ToolCalls.Add((callId, name, args));
                }
            }
        }
        return st;
    }

    private static string RenderUser(JsonElement data)
    {
        if (!data.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var part in c.EnumerateArray())
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var t)
                && t.GetString() == "text" && part.TryGetProperty("text", out var tx))
                sb.Append(tx.GetString());
        return sb.Length == 0 ? "" : "**你**\n\n" + sb.ToString().Trim() + "\n\n";
    }

    private static string CollectText(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var t)
                && t.GetString() == "text" && part.TryGetProperty("text", out var tx))
                sb.Append(tx.GetString());
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string Title(string t) => string.IsNullOrWhiteSpace(t) ? "DSH 会话导出" : t.Trim();
    private static string Str(JsonElement e, string prop, string fallback = "")
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static int Int(JsonElement e, string prop, int fallback = 0)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetInt64() : fallback;

    private sealed class Step
    {
        public string Text = "";
        public string Reasoning = "";
        public List<(string, string, string)> ToolCalls = new();
        public List<(string, bool, string)> Results = new();
    }

    private sealed class ExportState
    {
        public string Id = "", Cwd = "", Created = "", Title = "";
        public List<string> Users = new();
        public Dictionary<(int, int), Step> Steps = new();
        public List<(int, int)> StepOrder = new();
        public HashSet<(int, int)> StepSeen = new();
    }
}
