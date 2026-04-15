using System.Windows;
using System.Windows.Controls;

namespace TheAllocator.Pages;

public partial class StartPage : Page
{
    private readonly NavigatorWindow _shell;

    public StartPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
    }

    private void OldComputer_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupSelectUserPage();
    }

    private void NewComputer_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToRestoreSelectBackupFilePage();
    }
}
