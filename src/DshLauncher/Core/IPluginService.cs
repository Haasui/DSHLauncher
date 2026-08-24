namespace DshLauncher.Core;

/// <summary>插件信息。</summary>
public sealed record PluginInfo(
    string Id,
    string Name,
    string? Version,
    string? Description,
    string? InstalledPath,
    bool Isolated);

/// <summary>
/// 插件服务：列表 / 安装（npm/git）/ 更新 / 隔离（对齐 loudMore：目录挂 .disabled 后缀）/ 自检。
/// 目录 ~/.dsh/profiles/web/node_modules；安装/更新走官方 dsh plugin（pnpm）。
/// 全部 Try-Catch 容错（插件目录结构变化不崩溃）。
/// </summary>
public interface IPluginService
{
    Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(CancellationToken ct = default);

    Task<bool> InstallAsync(string source, CancellationToken ct = default);

    Task<bool> UpdateAsync(string id, CancellationToken ct = default);

    Task<bool> IsolateAsync(string id, CancellationToken ct = default);

    Task<bool> UnisolateAsync(string id, CancellationToken ct = default);

    /// <summary>卸载：官方 dsh plugin remove；失败则直接删除包目录（含 .disabled）。</summary>
    Task<bool> UninstallAsync(string id, CancellationToken ct = default);

    /// <summary>启动自检：package.json 缺失/损坏的插件自动挂 .disabled，返回被隔离的 id。</summary>
    Task<IReadOnlyList<string>> SelfCheckAsync(CancellationToken ct = default);

    /// <summary>读取依赖缺失清单（dependencies/peerDependencies 在本地不可解析的）。</summary>
    Task<IReadOnlyList<string>> GetMissingDependenciesAsync(string id, CancellationToken ct = default);

    /// <summary>生成修复命令文本（npm install -g 与本地 npm install）。</summary>
    Task<string> GetFixCommandsAsync(string id, CancellationToken ct = default);
}