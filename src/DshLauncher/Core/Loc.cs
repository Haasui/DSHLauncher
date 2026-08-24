using System.Collections.Generic;

namespace DshLauncher.Core;

/// <summary>
/// 轻量国际化：按语言返回 UI 文案。启动时由 App 依据设置设置 <see cref="Current"/>，
/// 后续可把各视图的硬编码中文逐个迁移到 <see cref="Get"/> 键上（当前已覆盖主导航/部分缺省文案）。
/// </summary>
public static class Loc
{
    public static string Current { get; set; } = "zh";

    private static readonly Dictionary<string, (string zh, string en)> Map = new()
    {
        ["nav.home"] = ("启动", "Home"),
        ["nav.settings"] = ("设置", "Settings"),
        ["nav.doctor"] = ("诊断", "Diagnostics"),
        ["nav.update"] = ("更新", "Update"),
        ["nav.plugin"] = ("插件", "Plugins"),
        ["nav.quick"] = ("速查", "Cheat Sheet"),
        ["nav.log"] = ("日志", "Logs"),
        ["nav.about"] = ("关于", "About"),
        ["app.name"] = ("DeepSeek Harness 启动器", "DeepSeek Harness Launcher"),
        ["app.brand"] = ("DSHLauncher", "DSHLauncher"),
    };

    public static string Get(string key)
        => Map.TryGetValue(key, out var v) ? (Current == "en" ? v.en : v.zh) : key;
}
