using System.Net.Http;
using System.Text.Json;

namespace DshLauncher.Core;

/// <summary>插件源候选（npm registry 搜索结果）。</summary>
public sealed record PluginCandidate(
    string Id,
    string Version,
    string? Description,
    string? Author,
    string? Repository,
    bool Installed,
    bool IsGitHub = false,
    int Stargazers = 0,
    IReadOnlyList<string>? Topics = null);

/// <summary>插件源分页结果：MarketTotal=市场总数，From=本页起始偏移，Items=本页条目，HasMore=是否还有下一页。</summary>
public sealed record StoreSearchResult(int MarketTotal, int From, IReadOnlyList<PluginCandidate> Items, bool HasMore);

/// <summary>
/// 插件源（lite）：对接 npm registry 官方搜索 API（keywords:dsh-plugin），
/// 本机实测可用（2283 个结果）。GitHub 本机不可达，暂不接。
/// </summary>
public sealed class PluginStoreService
{
    private static string SearchBase()
        => string.IsNullOrWhiteSpace(AppServices.Settings.NpmRegistry)
            ? "https://registry.npmjs.org/-/v1/search"
            : AppServices.Settings.NpmRegistry!.TrimEnd('/') + "/-/v1/search";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public const int PageSize = 50;

    public async Task<StoreSearchResult> SearchDshPluginsAsync(string? query, int from = 0, CancellationToken ct = default)
    {
        var list = new List<PluginCandidate>();
        var text = string.IsNullOrWhiteSpace(query)
            ? "keywords:dsh-plugin"
            : "keywords:dsh-plugin " + query.Trim();
        var url = $"{SearchBase()}?text={Uri.EscapeDataString(text)}&size={PageSize}&from={from}";
        int total = 0;
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return new StoreSearchResult(0, from, list, false);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number) total = (int)t.GetInt64();
            if (!doc.RootElement.TryGetProperty("objects", out var objects)) return new StoreSearchResult(total, from, list, false);
            foreach (var o in objects.EnumerateArray())
            {
                if (!o.TryGetProperty("package", out var pkg)) continue;
                var name = pkg.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var version = pkg.TryGetProperty("version", out var v) ? v.GetString() : "--";
                var desc = pkg.TryGetProperty("description", out var d) ? d.GetString() : null;
                var author = GetAuthor(pkg);
                var repo = "";
                if (pkg.TryGetProperty("repository", out var r)
                    && r.ValueKind == JsonValueKind.Object
                    && r.TryGetProperty("url", out var ru)) repo = ru.GetString() ?? "";
                list.Add(new PluginCandidate(name, version ?? "--", desc, author, repo, false));
            }
        }
        catch
        {
            // 网络失败 → 空列表，UI 提示
        }
        return new StoreSearchResult(total, from, list, from + PageSize < total);
    }

    /// <summary>
    /// 插件商城增强：合并 npm registry 与 GitHub（q=dsh-plugin 话题/描述）两个源，
    /// 按 id 去重，尽力而为；任一源不可达不影响另一源。
    /// </summary>
    /// <summary>插件市场：以 GitHub dsh-plugin 仓库为主（分页），首页并上 npm 包。</summary>
    public async Task<StoreSearchResult> SearchAllPluginsAsync(string? query, int page = 1, string sort = "stars", CancellationToken ct = default)
    {
        var git = await SearchGitHubDshPluginsAsync(query, page, sort, ct);
        var byId = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in git.Items) byId[c.Id] = c;
        // 首页同时并上 npm 包（dsh 插件多经 npm 发布，安装走包名）
        if (page == 1)
        {
            var npm = await SearchDshPluginsAsync(query, 0, ct);
            foreach (var c in npm.Items) byId.TryAdd(c.Id, c);
        }
        return new StoreSearchResult(git.MarketTotal, page, byId.Values.ToList(), git.HasMore);
    }

    public const int GitHubPageSize = 50;

    /// <summary>GitHub 仓库搜索（dsh-plugin 话题/描述），可按 star/updated 排序分页——这是 dsh 插件市场。</summary>
    public async Task<StoreSearchResult> SearchGitHubDshPluginsAsync(string? query, int page = 1, string sort = "stars", CancellationToken ct = default)
    {
        var list = new List<PluginCandidate>();
        var q = string.IsNullOrWhiteSpace(query) ? "dsh-plugin" : "dsh-plugin " + query.Trim();
        var sortParam = sort == "updated" ? "updated" : "stars";
        var url = "https://api.github.com/search/repositories?q=" + Uri.EscapeDataString(q)
                  + "&sort=" + sortParam + "&order=desc&per_page=" + GitHubPageSize + "&page=" + page;
        int total = 0;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("DshLauncher/2.2");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return new StoreSearchResult(0, 0, list, false);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("total_count", out var tc) && tc.ValueKind == JsonValueKind.Number) total = (int)tc.GetInt64();
            if (!doc.RootElement.TryGetProperty("items", out var items)) return new StoreSearchResult(total, 0, list, false);
            foreach (var it in items.EnumerateArray())
            {
                var full = it.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;
                if (string.IsNullOrWhiteSpace(full)) continue;
                var desc = it.TryGetProperty("description", out var d) ? d.GetString() : null;
                var author = "";
                if (it.TryGetProperty("owner", out var ow) && ow.TryGetProperty("login", out var lg)) author = lg.GetString() ?? "";
                var repo = it.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "";
                var stars = it.TryGetProperty("stargazers_count", out var sc) && sc.ValueKind == JsonValueKind.Number ? (int)sc.GetInt64() : 0;
                var topics = new List<string>();
                if (it.TryGetProperty("topics", out var tp) && tp.ValueKind == JsonValueKind.Array)
                    foreach (var t in tp.EnumerateArray()) if (t.ValueKind == JsonValueKind.String) topics.Add(t.GetString()!);
                list.Add(new PluginCandidate(full, "GitHub", desc, author, repo, false, IsGitHub: true, stars, topics));
            }
        }
        catch
        {
            // GitHub 不可达 → 忽略
        }
        return new StoreSearchResult(total, page, list, page * GitHubPageSize < total);
    }

    private static string? GetAuthor(JsonElement pkg)
    {
        if (!pkg.TryGetProperty("author", out var a)) return null;
        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString(),
            JsonValueKind.Object when a.TryGetProperty("name", out var an) => an.GetString(),
            _ => null,
        };
    }
}