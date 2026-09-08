using System.IO;
using System.Windows;
using System.Windows.Controls;
using TheAllocator.Services;

namespace TheAllocator.Pages;

public partial class RestoreCompletePage : Page
{
    private readonly NavigatorWindow _shell;

    public RestoreCompletePage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        var archiveSizeText = GetArchiveSizeText(_shell.Session.RestorePackagePath);
        var elapsedText = GetElapsedText(_shell.Session.RestoreStartedAt, _shell.Session.RestoreCompletedAt);
        CompleteText.Text =
            $"Restore package:\n{_shell.Session.RestorePackagePath}\n\n" +
            $"Archive size:\n{archiveSizeText}\n\n" +
            $"Elapsed time:\n{elapsedText}\n\n" +
            $"Target account:\n{_shell.Session.RestoreTargetAccountDisplay}\n\n" +
            $"Target profile folder:\n{_shell.Session.RestoreTargetProfilePath}\n\n" +
            $"Files restored: {_shell.Session.RestoreCopiedFileCount:N0}\n\n" +
            $"Selected printers for restore: {_shell.Session.SelectedRestorePrinters.Count}\n\n" +
            $"Restore log:\n{_shell.Session.RestoreLogPath}\n\n" +
            "Next steps:\n" +
            "1. Reboot this computer.\n" +
            "2. Sign in as the restored user.\n" +
            "3. Confirm the desktop loads and the expected files are present.\n" +
            "4. Confirm printers are available.\n\n" +
            "If sign-in fails, do not rerun the restore over the broken profile state. Reimage or fully clean the target profile state before testing again.";
    }

    private void StartOver_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToStartPage();
    }

    private void RebootComputer_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Restart this computer now? After reboot, sign in as the restored user to confirm the desktop loads.",
            "Reboot Computer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/r /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        _shell.Close();
    }

    private static string GetArchiveSizeText(string restorePackagePath)
    {
        if (string.IsNullOrWhiteSpace(restorePackagePath) || !File.Exists(restorePackagePath))
        {
            return "Unknown";
        }

        var size = new FileInfo(restorePackagePath).Length;
        return SizeFormattingService.ToReadableSize(size);
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
}
