using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DshLauncher.Core;
using DshLauncher.ViewModels;

namespace DshLauncher;

/// <summary>应用入口：单实例、crash.log、启动时加载设置、自动启动、托盘。</summary>
public partial class App : Application
{
    private const string MutexNamePrefix = @"Local\DshLauncher_SingleInstance_";
    private Mutex? _mutex;

    /// <summary>主 VM（托盘菜单/快捷操作需要）。</summary>
    public static MainViewModel? MainVm { get; set; }

    /// <summary>是否正在退出（关闭窗口不再最小化到托盘）。</summary>
    public static bool IsExiting { get; set; }

    /// <summary>托盘图标。</summary>
    public static System.Windows.Forms.NotifyIcon? Tray { get; private set; }

    /// <summary>灰度托盘图标缓存（DSH 未运行时用，运行中换彩色）。</summary>
    private static Icon? _grayIcon;

    /// <summary>应用版本号（来自程序集 2.0.0）。</summary>
    public static string Version
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    /// <summary>数据目录：%APPDATA%\DshLauncher。</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshLauncher");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：WaitOne(0) 模式，遗弃的 Mutex（上次强杀残留）也能正常接管
        _mutex = new Mutex(false, MutexNamePrefix + Environment.UserName);
        if (!_mutex.WaitOne(0, false))
        {
            MessageBox.Show("DeepSeek Harness 启动器已在运行。", "DeepSeek Harness 启动器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 崩溃日志（稳定性）
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
            MessageBox.Show("发生未处理异常，已写入 crash.log。\n" + args.Exception.Message,
                "DeepSeek Harness 启动器", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);

        // 加载设置后再显示主窗口（同步：StartupUri 窗口在 base.OnStartup 里创建，
        // 若异步等待，窗口会先于设置加载完成而建出，自动启动会读到默认值）
        AppServices.Settings.LoadAsync().GetAwaiter().GetResult();
        ThemeManager.Apply(ThemeManager.Resolve(AppServices.Settings.Theme)); // 浅/深色（支持实时热切）
        base.OnStartup(e);

        // 托盘
        SetupTray();
        AttachTrayStatus(); // 托盘悬停文字动态显示 DSH 状态
    }

    /// <summary>订阅 Home 状态变化，实时更新托盘悬停文字（状态/端口/模型）。</summary>
    private void AttachTrayStatus()
    {
        // 时序：base.OnStartup 创建的窗口/VM 可能尚未就绪，MainVm null 时延迟重试
        if (MainVm?.Home is not { } home)
        {
            Dispatcher.BeginInvoke(new Action(AttachTrayStatus), DispatcherPriority.ContextIdle);
            return;
        }
        home.PropertyChanged += (_, e) =>
        {
            // 白名单：只在状态/统计变化时刷新，避免 uptime 每秒刷屏
            if (e.PropertyName is "State" or "StatusText" or "StatusDetail" or "SessionCount" or "WorkspaceCount")
                UpdateTrayStatus(home);
        };
        UpdateTrayStatus(home);
    }

    /// <summary>构建并写入托盘悬停文字 + 按状态切换图标颜色（运行=彩色，其他=灰度）。</summary>
    private static void UpdateTrayStatus(HomeViewModel home)
    {
        try
        {
            if (Tray is not { } tray) return;
            var running = home.State == DshState.Running;
            // 统一鲸鱼娘梗（社区二创：蓝色大肥鱼，爱吃用户白饭）——不掺技术内容
            var text = home.State switch
            {
                DshState.Running => "鲸鱼娘正在吃白饭",
                DshState.Starting => "鲸鱼娘准备干饭…",
                DshState.Error => "鲸鱼娘噎着了",
                _ => "鲸鱼娘饿晕了，双击喂饭",
            };
            tray.Text = text;

            // 图标颜色：运行中彩色，否则灰度（切换一次，缓存灰度实例）
            if (running)
            {
                if (tray.Icon is null || ReferenceEquals(tray.Icon, _grayIcon))
                    tray.Icon = LoadTrayIcon();
            }
            else
            {
                _grayIcon ??= ToGrayscale(LoadTrayIcon());
                if (!ReferenceEquals(tray.Icon, _grayIcon)) tray.Icon = _grayIcon;
            }
        }
        catch
        {
            // 托盘更新失败不崩溃
        }
    }

    /// <summary>把图标转灰度（ColorMatrix 亮度保留），供未运行/异常状态使用。</summary>
    private static Icon ToGrayscale(Icon source)
    {
        using var bmp = source.ToBitmap();
        using var gray = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(gray))
        {
            var cm = new ColorMatrix(new[]
            {
                new[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
                new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
                new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            });
            var attrs = new ImageAttributes();
            attrs.SetColorMatrix(cm);
            g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
                0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
        }
        return Icon.FromHandle(gray.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Tray?.Dispose(); } catch { }
        try { AppServices.Status.Stop(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    private void SetupTray()
    {
        try
        {
            Tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Visible = true,
                Text = "DeepSeek Harness 启动器",
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("一键启动 DeepSeek Harness", null, (_, _) => RunOnUi(() => MainVm?.Home.StartCommand.Execute(null)));
            menu.Items.Add("停止 DeepSeek Harness", null, (_, _) => RunOnUi(() => MainVm?.Home.StopCommand.Execute(null)));
            menu.Items.Add("打开 DeepSeek Harness 界面", null, (_, _) => RunOnUi(() => MainVm?.Home.OpenInterface()));
            menu.Items.Add("切换 DeepSeek Harness 主题", null, (_, _) => RunOnUi(() => MainVm?.StatusBar.ToggleThemeCommand.Execute(null)));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) =>
            {
                IsExiting = true;
                Shutdown();
            });
            Tray.ContextMenuStrip = menu;
            Tray.DoubleClick += (_, _) => ShowMainWindow();
            Notifier.BindBalloonClick(); // 审批通知气泡点击回调
        }
        catch
        {
            // 托盘失败不崩溃
        }
    }

    private void ShowMainWindow()
    {
        RunOnUi(() =>
        {
            var w = MainWindow;
            if (w is null) return;
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        });
    }

    private void RunOnUi(Action action) => Dispatcher.Invoke(action);

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info?.Stream is null) return System.Drawing.SystemIcons.Application;
            return new System.Drawing.Icon(info.Stream);
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // 写日志失败不再抛
        }
    }
}