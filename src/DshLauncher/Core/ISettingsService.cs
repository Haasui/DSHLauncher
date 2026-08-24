namespace DshLauncher.Core;

/// <summary>
/// 设置服务：读写 %APPDATA%\DshLauncher\config.json。
/// </summary>
public interface ISettingsService
{
    /// <summary>DSH Web 端口（默认 3080）。</summary>
    int Port { get; set; }

    /// <summary>spawn 时附加的启动参数。</summary>
    string? ExtraArgs { get; set; }

    /// <summary>官方 --patch 覆盖层文件路径（可选；路径不能含空格/引号）。</summary>
    string? PatchFile { get; set; }

    /// <summary>打开启动器时自动启动 DSH。</summary>
    bool AutoStartOnLaunch { get; set; }

    /// <summary>开机自启（HKCU Run）。</summary>
    bool StartWithWindows { get; set; }

    /// <summary>关闭主窗口时最小化到托盘。</summary>
    bool MinimizeToTray { get; set; }

    /// <summary>打开 DSH 界面时用系统浏览器（默认开；否则 WebView2 嵌入）。</summary>
    bool OpenInBrowser { get; set; }

    /// <summary>守护模式：DSH 异常退出时自动重启（默认开）。</summary>
    bool AutoRestartOnCrash { get; set; }

    /// <summary>启动器主题：light | dark。</summary>
    string Theme { get; set; }

    /// <summary>npm 镜像/代理，空用官方源。</summary>
    string? NpmRegistry { get; set; }

    /// <summary>一言语录接口（空=默认国际镜像）。</summary>
    string? QuoteApiUrl { get; set; }

    /// <summary>界面语言：zh | en。</summary>
    string Language { get; set; }

    /// <summary>配置文件路径。</summary>
    string ConfigPath { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
