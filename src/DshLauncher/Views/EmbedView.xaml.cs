using System.IO;
using System.Windows;
using System.Windows.Controls;
using DshLauncher.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace DshLauncher.Views;

public partial class EmbedView : UserControl
{
    public EmbedView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmbedViewModel vm) return;
        await vm.OpenAsync();
        if (vm.ShowEmbed && vm.Url is string url)
        {
            try
            {
                _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
                // 独立 userDataFolder：与默认浏览器/其它应用隔离（官方友好、安全）
                var env = await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(DshLauncher.App.DataDir, "WebView2"));
                await Web.EnsureCoreWebView2Async(env);
                Web.Source = new Uri(url);
            }
            catch (Exception ex)
            {
                vm.SetError("未检测到 WebView2 运行时，请安装 Microsoft Edge WebView2 Evergreen Runtime。\n" + ex.Message);
            }
        }
    }
}