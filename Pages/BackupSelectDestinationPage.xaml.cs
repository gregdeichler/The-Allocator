using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace TheAllocator.Pages;

public partial class BackupSelectDestinationPage : Page
{
    private readonly NavigatorWindow _shell;

    public BackupSelectDestinationPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadState();
    }

    private void LoadState()
    {
        var profile = _shell.Session.SelectedBackupProfile;

        if (string.IsNullOrWhiteSpace(_shell.Session.BackupPackageName) && profile is not null)
        {
            _shell.Session.BackupPackageName = _shell.SevenZipService.GetRecommendedArchiveName(profile.UserName);
        }

        _shell.Session.BackupMetadataFileName = GetMetadataFileName(profile?.UserName);
        _shell.Session.BackupPrintersFileName = GetPrintersFileName(profile?.UserName);
        _shell.Session.BackupLogFileName = GetBackupLogFileName(profile?.UserName);

        DestinationTextBox.Text = _shell.Session.BackupDestinationFolder;
        RefreshOutputSummary();
        CompressionSummaryText.Text = _shell.SevenZipService.GetCompressionSummary();
        UpdateCanContinue();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the backup destination folder",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            DestinationTextBox.Text = dialog.SelectedPath;
            _shell.Session.BackupDestinationFolder = dialog.SelectedPath;
            RefreshOutputSummary();
            UpdateCanContinue();
        }
    }

    private void DestinationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _shell.Session.BackupDestinationFolder = DestinationTextBox.Text.Trim();
        RefreshOutputSummary();
        UpdateCanContinue();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupSelectPrintersPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupReviewPage();
    }

    private void UpdateCanContinue()
    {
        NextButton.IsEnabled = !string.IsNullOrWhiteSpace(DestinationTextBox.Text);
    }

    private void RefreshOutputSummary()
    {
        var profile = _shell.Session.SelectedBackupProfile;
        var backupFolderName = GetBackupFolderName(profile?.UserName);
        var backupFolderPreview = string.IsNullOrWhiteSpace(_shell.Session.BackupDestinationFolder)
            ? backupFolderName
            : Path.Combine(_shell.Session.BackupDestinationFolder, backupFolderName);

        OutputSummaryText.Text =
            $"Backup folder: {backupFolderPreview}\n" +
            $"Archive file: {_shell.Session.BackupPackageName}\n" +
            $"Backup details file: {_shell.Session.BackupMetadataFileName}\n" +
            $"Printers file: {_shell.Session.BackupPrintersFileName}\n" +
            $"Text log file: {_shell.Session.BackupLogFileName}\n" +
            "Logs folder: logs\\";
    }

    private static string GetMetadataFileName(string? userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return $"{safeUserName}-backup.json";
    }

    private static string GetPrintersFileName(string? userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return $"{safeUserName}-printers.json";
    }

    private static string GetBackupLogFileName(string? userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return $"{safeUserName}-backup-log.txt";
    }

    private static string GetBackupFolderName(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
}
