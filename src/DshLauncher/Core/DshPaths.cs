using System.IO;

namespace DshLauncher.Core;

/// <summary>
/// dsh 数据目录解析（对齐官方 dsh-home-paths）：优先级 = 显式配置 > $DSH_HOME（空白视为未设置）> ~/.dsh。
/// 皮肤/插件/配置路径一律经由此类解析，避免硬编码 ~/.dsh。
/// </summary>
public static class DshPaths
{
    public static string Home
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                try { return Path.GetFullPath(env.Trim()); }
                catch { /* 非法路径回退默认 */ }
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        }
    }

    public static string ProfilesDir => Path.Combine(Home, "profiles");
    public static string WebProfileDir => Path.Combine(ProfilesDir, "web");
    public static string WebPackageJson => Path.Combine(WebProfileDir, "package.json");
    public static string WebNodeModules => Path.Combine(WebProfileDir, "node_modules");
    public static string FlatNodeModules => Path.Combine(ProfilesDir, "node_modules");
}