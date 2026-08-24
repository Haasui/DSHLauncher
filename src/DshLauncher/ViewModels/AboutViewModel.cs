using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace DshLauncher.ViewModels;

/// <summary>关于页：版本 / 运行时 / 路径 / 项目主页 / 致谢。</summary>
public partial class AboutViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string AppVersion => DshLauncher.App.Version;

    public string Runtime => $"Windows {Environment.OSVersion.Version} · .NET {Environment.Version}";

    public string AppDir => AppContext.BaseDirectory;

    /// <summary>启动器项目主页（GitHub 仓库）。</summary>
    public string RepositoryUrl => Core.LauncherUpdater.RepositoryUrl;

    [RelayCommand]
    private void OpenRepository()
    {
        if (!string.IsNullOrEmpty(RepositoryUrl))
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
    }
}
