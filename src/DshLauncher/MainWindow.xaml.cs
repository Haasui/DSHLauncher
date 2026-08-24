using System.Windows;
using DshLauncher.Core;
using DshLauncher.ViewModels;

namespace DshLauncher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        App.MainVm = (MainViewModel)DataContext;
        try
        {
            // 用多帧 app.ico 作标题栏源：WPF 自动取 16px 帧，标题栏大小正确（
            // 不用 256px PNG 硬缩；托盘/任务栏也走同一 ico 的合适帧）
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        }
        catch { }
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting) return;
        if (AppServices.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }
}