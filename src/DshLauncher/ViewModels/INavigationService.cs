namespace DshLauncher.ViewModels;

/// <summary>页面跳转（启动页「异常 → 前往诊断」「已运行 → 打开嵌入界面」等）。</summary>
public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>显示 WebView2 嵌入页（DSH 界面）。</summary>
    void ShowEmbed();
}
