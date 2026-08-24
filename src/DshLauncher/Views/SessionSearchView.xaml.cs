using System.Windows.Controls;
using System.Windows.Input;
using DshLauncher.ViewModels;

namespace DshLauncher.Views;

public partial class SessionSearchView : UserControl
{
    public SessionSearchView()
    {
        InitializeComponent();
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SessionSearchViewModel vm)
            vm.RunCommand.Execute(null);
    }
}
