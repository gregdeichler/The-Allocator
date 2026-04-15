using System.Windows;
using System.Windows.Controls;
using TheAllocator.Services;

namespace TheAllocator.Pages;

public partial class RestoreProgressPage : Page
{
    private readonly NavigatorWindow _shell;
    private bool _hasStarted;
    private bool _restoreSucceeded;

    public RestoreProgressPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        ProgressText.Text =
            $"Restore package:\n{_shell.Session.RestorePackagePath}\n\n" +
            $"Target account:\n{_shell.Session.RestoreTargetUser}\n\n" +
            $"Existing profile behavior:\n{_shell.Session.RestoreCollisionMode}\n\n" +
            "Preparing restore...";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        var progress = new Progress<string>(message => ProgressText.Text = message);
        var result = await _shell.RestoreService.RestoreAsync(_shell.Session, progress);

        if (result.Success)
        {
            _restoreSucceeded = true;
            _shell.Session.RestoreErrorMessage = string.Empty;
            _shell.Session.RestoreLogPath = result.RestoreLogPath;
            _shell.Session.RestoreTargetProfilePath = result.TargetProfilePath;
            _shell.Session.RestoreCopiedFileCount = result.CopiedFileCount;
            StatusTitleText.Text = "Restore Complete";
            RestoreProgressBar.IsIndeterminate = false;
            RestoreProgressBar.Value = 100;
            ProgressText.Text =
                $"Files restored into:\n{result.TargetProfilePath}\n\n" +
                $"Restore log:\n{result.RestoreLogPath}\n\n" +
                $"Copied files: {result.CopiedFileCount:N0}";
            FinishButton.IsEnabled = true;
            return;
        }

        _shell.Session.RestoreErrorMessage = result.ErrorMessage;
        _shell.Session.RestoreLogPath = result.RestoreLogPath;
        StatusTitleText.Text = "Restore Failed";
        RestoreProgressBar.IsIndeterminate = false;
        RestoreProgressBar.Value = 100;
        ProgressText.Text =
            $"The restore did not complete.\n\n" +
            $"Error:\n{result.ErrorMessage}\n\n" +
            (!string.IsNullOrWhiteSpace(result.RestoreLogPath)
                ? $"Restore log:\n{result.RestoreLogPath}"
                : "Check the selected backup package and try again.");
        FinishButton.Content = "Back To Review";
        FinishButton.IsEnabled = true;
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (!_restoreSucceeded)
        {
            _shell.GoToRestoreReviewPage();
            return;
        }

        _shell.Session.RestoreCompletedAt = DateTime.Now;
        _shell.GoToRestoreCompletePage();
    }
}
