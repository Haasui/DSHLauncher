using System.IO;
using System.Text;
using System.Text.Json;

namespace DshLauncher.Core;

/// <summary>
/// 插件服务：列出 ~/.dsh/profiles/web/node_modules 插件；安装/更新走官方 dsh plugin（pnpm）；
/// 隔离 = 同目录改名 <pkg> 为 <pkg>.disabled（对齐 loudMore，P-D）；下划线前缀目录视为内部辅助不算插件。
/// 只读操作默认真实可用；安装/更新/隔离会修改用户 profile，需在应用内由用户触发。
/// </summary>
public sealed class PluginService : IPluginService
{
    private static readonly TimeSpan OpTimeout = TimeSpan.FromMinutes(5);
    private static readonly string WebNodeModules = DshPaths.WebNodeModules;
    private const string DisabledSuffix = ".disabled";

    public Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(CancellationToken ct = default)
    {
        var list = new List<PluginInfo>();
        if (!Directory.Exists(WebNodeModules)) return Task.FromResult<IReadOnlyList<PluginInfo>>(list);
        foreach (var sub in Directory.EnumerateDirectories(WebNodeModules, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('_') || name.StartsWith('.')) continue;
            if (name.StartsWith('@'))
            {
                foreach (var scoped in Directory.EnumerateDirectories(sub, "*", SearchOption.TopDirectoryOnly))
                    Add(list, scoped);
            }
            else
            {
                Add(list, sub);
            }
        }
        return Task.FromResult<IReadOnlyList<PluginInfo>>(list);
    }

    private static void Add(List<PluginInfo> list, string dir)
    {
        try
        {
            var leaf = Path.GetFileName(dir);
            var isolated = leaf.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            var id = isolated ? leaf[..^DisabledSuffix.Length] : leaf;
            string? version = null;
            string? description = null;
            var pj = Path.Combine(dir, "package.json");
            if (File.Exists(pj))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pj, Encoding.UTF8));
                version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
                description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;
            }
            list.Add(new PluginInfo(id, id, version, description, dir, isolated));
        }
        catch
        {
            // 容错：目录结构变化不崩溃
        }
    }

    public async Task<bool> InstallAsync(string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var r = await CommandRunner.RunAsync($"dsh plugin --profile web add {Quote(source)}", OpTimeout, ct);
        return r.ExitCode == 0;
    }

    public async Task<bool> UpdateAsync(string id, CancellationToken ct = default)
    {
        var r = await CommandRunner.RunAsync($"dsh plugin --profile web update {Quote(id)}", OpTimeout, ct);
        return r.ExitCode == 0;
    }

    public async Task<bool> UninstallAsync(string id, CancellationToken ct = default)
    {
        // 先解析真实安装目录（含 scoped 包与 .disabled 隔离项），别用裸 id 拼路径（scoped 包会丢 @scope）
        var dirs = ResolveDirs(id).ToList();
        var pkgId = DerivePackageId(id, dirs.FirstOrDefault());
        // 官方 remove（清理 profile 依赖声明）；对非声明依赖会失败 → 走物理删除兜底
        var r = await CommandRunner.RunAsync($"dsh plugin --profile web remove {Quote(pkgId)}", OpTimeout, ct);
        var any = false;
        foreach (var d in dirs)
        {
            try { DeleteDirSafe(d); any = true; }
            catch { }
        }
        return r.ExitCode == 0 || any;
    }

    public Task<bool> IsolateAsync(string id, CancellationToken ct = default)
        => Task.Run(() => Rename(id, toIsolated: true), ct);

    public Task<bool> UnisolateAsync(string id, CancellationToken ct = default)
        => Task.Run(() => Rename(id, toIsolated: false), ct);

    private static bool Rename(string id, bool toIsolated)
    {
        try
        {
            var active = ActivePath(id);
            var src = toIsolated ? active : active + DisabledSuffix;
            var dest = toIsolated ? active + DisabledSuffix : active;
            if (!Directory.Exists(src)) return false;
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.Move(src, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> SelfCheckAsync(CancellationToken ct = default)
    {
        var quarantined = new List<string>();
        if (!Directory.Exists(WebNodeModules)) return quarantined;
        foreach (var sub in Directory.EnumerateDirectories(WebNodeModules, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('_') || name.StartsWith('.')) continue;
            if (name.StartsWith('@'))
            {
                foreach (var scoped in Directory.EnumerateDirectories(sub, "*", SearchOption.TopDirectoryOnly))
                {
                    var leaf = Path.GetFileName(scoped);
                    if (leaf.StartsWith('_') || leaf.StartsWith('.')) continue;
                    await CheckAsync(scoped, quarantined, ct);
                }
            }
            else
            {
                await CheckAsync(sub, quarantined, ct);
            }
        }
        return quarantined;
    }

    private static async Task CheckAsync(string dir, List<string> quarantined, CancellationToken ct)
    {
        var leaf = Path.GetFileName(dir);
        if (leaf.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)) return;
        if (IsValidPackage(dir)) return;
        // 损坏 → 自动隔离
        await Task.Run(() =>
        {
            try
            {
                var dest = Path.Combine(Path.GetDirectoryName(dir)!, leaf + DisabledSuffix);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.Move(dir, dest);
                quarantined.Add(leaf);
            }
            catch { }
        }, ct);
    }

    private static bool IsValidPackage(string dir)
    {
        var pj = Path.Combine(dir, "package.json");
        try
        {
            if (!File.Exists(pj)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(pj, Encoding.UTF8));
            return doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<string>> GetMissingDependenciesAsync(string id, CancellationToken ct = default)
    {
        var missing = new List<string>();
        var dir = ActivePath(id);
        if (Directory.Exists(dir))
        {
            var pj = Path.Combine(dir, "package.json");
            if (File.Exists(pj))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(pj, Encoding.UTF8));
                    var root = doc.RootElement;
                    foreach (var key in new[] { "dependencies", "peerDependencies" })
                    {
                        if (!root.TryGetProperty(key, out var deps) || deps.ValueKind != JsonValueKind.Object) continue;
                        foreach (var dep in deps.EnumerateObject())
                        {
                            if (!DependencyExists(dep.Name)) missing.Add(dep.Name);
                        }
                    }
                }
                catch { }
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(missing);
    }

    private static bool DependencyExists(string dep)
    {
        foreach (var baseDir in new[] { WebNodeModules, DshPaths.FlatNodeModules })
        {
            var p = dep.StartsWith('@')
                ? Path.Combine(baseDir, dep.Split('/')[0], dep[(dep.IndexOf('/') + 1)..])
                : Path.Combine(baseDir, dep);
            if (Directory.Exists(p)) return true;
        }
        return false;
    }

    public async Task<string> GetFixCommandsAsync(string id, CancellationToken ct = default)
    {
        var missing = await GetMissingDependenciesAsync(id, ct);
        if (missing.Count == 0) return "依赖完整，无需修复。";
        var lines = missing.Select(d => $"npm install -g {d}");
        var dir = ActivePath(id);
        return string.Join("\n", lines.Append($"cd \"{dir}\" && npm install"));
    }

    /// <summary>插件的激活目录（非 .disabled 优先）；scoped 包解析到 @scope/pkg，找不到则回退规范路径。</summary>
    private static string ActivePath(string id)
        => ResolveDirs(id).FirstOrDefault(d => !d.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
           ?? Path.Combine(WebNodeModules, id.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>找出插件的真实安装目录（含 scoped 包与 .disabled 隔离项），可能多个。</summary>
    private static IEnumerable<string> ResolveDirs(string id)
    {
        var rel = id.Replace('/', Path.DirectorySeparatorChar);
        var direct = Path.Combine(WebNodeModules, rel);
        if (Directory.Exists(direct)) yield return direct;
        if (Directory.Exists(direct + DisabledSuffix)) yield return direct + DisabledSuffix;
        foreach (var scope in Directory.EnumerateDirectories(WebNodeModules, "@*", SearchOption.TopDirectoryOnly))
        {
            var scoped = Path.Combine(scope, rel);
            if (Directory.Exists(scoped)) yield return scoped;
            if (Directory.Exists(scoped + DisabledSuffix)) yield return scoped + DisabledSuffix;
        }
    }

    /// <summary>用已安装目录的 package.json name 取真实包 id（可能带 @scope），供 dsh plugin remove 使用。</summary>
    private static string DerivePackageId(string id, string? dir = null)
    {
        var resolved = dir ?? ResolveDirs(id).FirstOrDefault();
        if (string.IsNullOrEmpty(resolved)) return id;
        var pj = Path.Combine(resolved, "package.json");
        if (File.Exists(pj))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pj, Encoding.UTF8));
                var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
        }
        return id;
    }

    /// <summary>删除目录：junction/symlink 只删链接不递归（避免误删 pnpm store 目标），普通目录递归删除。</summary>
    private static void DeleteDirSafe(string dir)
    {
        if (!Directory.Exists(dir)) return;
        FileAttributes attrs;
        try { attrs = File.GetAttributes(dir); }
        catch { attrs = FileAttributes.Normal; }
        var isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
        if (isReparse) Directory.Delete(dir, false);   // 只删链接，保留 pnpm store 目标
        else Directory.Delete(dir, true);
    }

    private static string Quote(string s)
        => s.Contains(' ') || s.Contains('&') ? $"\"{s.Trim('\"')}\"": s.Trim();
}