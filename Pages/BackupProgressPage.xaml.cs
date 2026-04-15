using System.Windows;
using System.Windows.Controls;
using TheAllocator.Services;

namespace TheAllocator.Pages;

public partial class BackupProgressPage : Page
{
    private readonly NavigatorWindow _shell;
    private bool _hasStarted;
    private bool _backupSucceeded;

    public BackupProgressPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        ProgressText.Text = $"Backup archive path:\n{_shell.Session.BackupPackagePath}\n\nPreparing backup...";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        var progress = new Progress<string>(message => ProgressText.Text = message);
        var result = await _shell.BackupService.CreateBackupAsync(_shell.Session, progress);

        if (result.Success)
        {
            _backupSucceeded = true;
            _shell.Session.BackupArchiveSizeBytes = result.ArchiveSizeBytes;
            _shell.Session.BackupErrorMessage = string.Empty;
            StatusTitleText.Text = "Backup Complete";
            BackupProgressBar.IsIndeterminate = false;
            BackupProgressBar.Value = 100;
            ProgressText.Text =
                $"Backup archive created:\n{result.ArchivePath}\n\n" +
                $"Metadata file:\n{result.MetadataPath}\n\n" +
                $"Printers file:\n{result.PrintersPath}\n\n" +
                $"Log file:\n{result.LogPath}\n\n" +
                $"Archive size: {SizeFormattingService.ToReadableSize(result.ArchiveSizeBytes)}\n" +
                $"Copied files: {result.CopiedFileCount:N0}";
            FinishButton.IsEnabled = true;
            return;
        }

        _shell.Session.BackupErrorMessage = result.ErrorMessage;
        StatusTitleText.Text = "Backup Failed";
        BackupProgressBar.IsIndeterminate = false;
        BackupProgressBar.Value = 100;
        ProgressText.Text =
            $"The backup did not complete.\n\n" +
            $"Error:\n{result.ErrorMessage}\n\n" +
            $"Check the destination folder and try again.";
        FinishButton.Content = "Back To Review";
        FinishButton.IsEnabled = true;
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (!_backupSucceeded)
        {
            _shell.GoToBackupReviewPage();
            return;
        }

        _shell.Session.BackupCompletedAt = DateTime.Now;
        _shell.GoToBackupCompletePage();
    }
}
