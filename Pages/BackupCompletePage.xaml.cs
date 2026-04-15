using System.Windows;
using System.Windows.Controls;
using TheAllocator.Services;

namespace TheAllocator.Pages;

public partial class BackupCompletePage : Page
{
    private readonly NavigatorWindow _shell;

    public BackupCompletePage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        var elapsedText = GetElapsedText(_shell.Session.BackupStartedAt, _shell.Session.BackupCompletedAt);
        CompleteText.Text =
            $"Expected backup package:\n{_shell.Session.BackupPackagePath}\n\n" +
            $"Archive size:\n{GetArchiveSizeText(_shell.Session.BackupArchiveSizeBytes)}\n\n" +
            $"Elapsed time:\n{elapsedText}\n\n" +
            "Next step:\nMove this package to the new computer and choose 'This is the new computer'.";
    }

    private void StartOver_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToStartPage();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _shell.Close();
    }

    private static string GetElapsedText(DateTime? startedAt, DateTime? completedAt)
    {
        if (startedAt is null || completedAt is null || completedAt < startedAt)
        {
            return "Unknown";
        }

        var elapsed = completedAt.Value - startedAt.Value;
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} sec"
            : $"{elapsed.Seconds} sec";
    }

    private static string GetArchiveSizeText(long archiveSizeBytes) =>
        archiveSizeBytes <= 0 ? "Unknown" : SizeFormattingService.ToReadableSize(archiveSizeBytes);
}
