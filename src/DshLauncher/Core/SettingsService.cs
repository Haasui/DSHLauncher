using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DshLauncher.Core;

/// <summary>设置：%APPDATA%\DshLauncher\config.json + HKCU Run 开机自启。</summary>
public sealed class SettingsService : ISettingsService
{
    private const string RegRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegRunValue = "DSHLauncher";

    public int Port { get; set; } = 3080;

    public string? ExtraArgs { get; set; }

    public string? PatchFile { get; set; }

    public bool AutoStartOnLaunch { get; set; }

    public bool StartWithWindows { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool OpenInBrowser { get; set; } = true;

    public bool AutoRestartOnCrash { get; set; } = true;

    /// <summary>启动后等待端口就绪上限（秒，默认 120）。</summary>
    public int StartupWaitSeconds { get; set; } = 120;


    /// <summary>启动器主题：light | dark。</summary>
    public string Theme { get; set; } = "light";

    /// <summary>npm 镜像/代理，空则用官方源。例 https://registry.npmmirror.com。</summary>
    public string? NpmRegistry { get; set; }

    /// <summary>一言语录接口（空=默认国际镜像 https://international.v1.hitokoto.cn/）。</summary>
    public string? QuoteApiUrl { get; set; }

    public string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DshLauncher", "config.json");

    public Task LoadAsync(CancellationToken ct = default)
    {
        // 同步实现（配置文件极小）：保证启动路径 GetAwaiter().GetResult() 不死锁
        try
        {
            if (!File.Exists(ConfigPath)) return Task.CompletedTask;
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data is null) return Task.CompletedTask;
            if (data.Port is > 0 and <= 65535) Port = data.Port.Value;
            ExtraArgs = string.IsNullOrWhiteSpace(data.ExtraArgs) ? null : data.ExtraArgs!.Trim();
            PatchFile = string.IsNullOrWhiteSpace(data.PatchFile) ? null : data.PatchFile!.Trim();
            AutoStartOnLaunch = data.AutoStartOnLaunch;
            StartWithWindows = data.StartWithWindows;
            MinimizeToTray = data.MinimizeToTray;
            OpenInBrowser = data.OpenInBrowser;
            AutoRestartOnCrash = data.AutoRestartOnCrash;
            if (data.StartupWaitSeconds is > 0 and <= 3600) StartupWaitSeconds = data.StartupWaitSeconds.Value;
            if (data.Theme is "light" or "dark") Theme = data.Theme;
            NpmRegistry = string.IsNullOrWhiteSpace(data.NpmRegistry) ? null : data.NpmRegistry!.Trim();
            QuoteApiUrl = string.IsNullOrWhiteSpace(data.QuoteApiUrl) ? null : data.QuoteApiUrl!.Trim();
        }
        catch (Exception ex)
        {
            // 配置损坏不崩溃，回退默认值
            System.Diagnostics.Debug.WriteLine($"加载配置失败：{ex.Message}");
        }
        return Task.CompletedTask;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var data = new SettingsData
        {
            Port = Port,
            ExtraArgs = ExtraArgs,
            PatchFile = PatchFile,
            AutoStartOnLaunch = AutoStartOnLaunch,
            StartWithWindows = StartWithWindows,
            MinimizeToTray = MinimizeToTray,
            OpenInBrowser = OpenInBrowser,
            AutoRestartOnCrash = AutoRestartOnCrash,
            StartupWaitSeconds = StartupWaitSeconds,
            Theme = Theme,
            NpmRegistry = NpmRegistry,
            QuoteApiUrl = QuoteApiUrl,
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ConfigPath, json, ct);

        ApplyStartWithWindows();
    }

    private void ApplyStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegRunKey);
            if (key is null) return;
            if (StartWithWindows)
            {
                var exe = Environment.ProcessPath
                          ?? Path.Combine(AppContext.BaseDirectory, "DshLauncher.exe");
                key.SetValue(RegRunValue, $"\"{exe}\"");
            }
            else if (key.GetValue(RegRunValue) is not null)
            {
                key.DeleteValue(RegRunValue, false);
            }
        }
        catch
        {
            // 注册表写入失败不崩溃
        }
    }

    private sealed class SettingsData
    {
        public int? Port { get; set; }
        public string? ExtraArgs { get; set; }
        public string? PatchFile { get; set; }
        public bool AutoStartOnLaunch { get; set; }
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool OpenInBrowser { get; set; }
        public bool AutoRestartOnCrash { get; set; }
        public int? StartupWaitSeconds { get; set; }
        public string Theme { get; set; } = "light";
        public bool? FollowWebTheme { get; set; }
        public string? NpmRegistry { get; set; }
        public string? QuoteApiUrl { get; set; }
    }
}
