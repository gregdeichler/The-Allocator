using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TheAllocator.Pages;

public partial class BackupReviewPage : Page
{
    private readonly NavigatorWindow _shell;

    public BackupReviewPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadSummary();
    }

    private void LoadSummary()
    {
        var profile = _shell.Session.SelectedBackupProfile;
        var printerCount = _shell.Session.SelectedBackupPrinters.Count;
        var outputFolder = GetBackupOutputFolder();

        SummaryText.Text =
            $"User: {profile?.DisplayName ?? "None selected"}\n" +
            $"Profile path: {profile?.ProfilePath ?? "Unknown"}\n" +
            $"Printers selected: {printerCount}\n" +
            $"Destination: {_shell.Session.BackupDestinationFolder}\n" +
            $"Backup folder: {outputFolder}\n" +
            $"Archive file: {_shell.Session.BackupPackageName}\n" +
            $"Backup details file: {_shell.Session.BackupMetadataFileName}\n" +
            $"Printers file: {_shell.Session.BackupPrintersFileName}\n" +
            $"Text log file: {_shell.Session.BackupLogFileName}\n" +
            "Logs folder: logs\\";
        UpdateCanBegin();
    }

    private void UpdateCanBegin()
    {
        BeginButton.IsEnabled =
            _shell.Session.SelectedBackupProfile is not null &&
            !string.IsNullOrWhiteSpace(_shell.Session.BackupDestinationFolder);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupSelectDestinationPage();
    }

    private void Begin_Click(object sender, RoutedEventArgs e)
    {
        _shell.Session.BackupPackagePath = Path.Combine(GetBackupOutputFolder(), _shell.Session.BackupPackageName);
        _shell.Session.BackupStartedAt = DateTime.Now;
        _shell.Session.BackupCompletedAt = null;
        _shell.Session.BackupJobId = Guid.NewGuid().ToString("N");
        _shell.GoToBackupProgressPage();
    }

    private string GetBackupOutputFolder()
    {
        var userName = _shell.Session.SelectedBackupProfile?.UserName;
        var folderName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return Path.Combine(_shell.Session.BackupDestinationFolder, folderName);
    }
}
