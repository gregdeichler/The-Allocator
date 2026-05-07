using System.IO;
using System.Windows;
using System.Windows.Controls;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class RestoreReviewPage : Page
{
    private readonly NavigatorWindow _shell;

    public RestoreReviewPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        var sourceUser = _shell.Session.RestoreManifest?.UserName ?? "Unknown";
        var comparableSourceUser = GetComparableAccountName(sourceUser);
        var comparableTargetUser = GetComparableAccountName(_shell.Session.RestoreTargetUser);
        var isCrossUserRestore = !string.IsNullOrWhiteSpace(_shell.Session.RestoreTargetUser) &&
                                 !string.Equals(comparableSourceUser, comparableTargetUser, StringComparison.OrdinalIgnoreCase);
        var targetProfilePath = _shell.Session.RestoreUseExistingAccount && _shell.Session.SelectedRestoreExistingProfile is not null
            ? _shell.Session.SelectedRestoreExistingProfile.ProfilePath
            : System.IO.Path.Combine(GetProfilesRootPath(), _shell.Session.RestoreTargetUser);
        var targetProfileExists = Directory.Exists(targetProfilePath);

        if (!targetProfileExists && _shell.Session.RestoreCollisionMode == RestoreCollisionMode.MergeIntoExistingProfile)
        {
            _shell.Session.RestoreCollisionMode = isCrossUserRestore
                ? RestoreCollisionMode.MergeIntoExistingProfile
                : RestoreCollisionMode.OverwriteExistingProfile;
        }

        SummaryText.Text =
            $"Backup package: {_shell.Session.RestorePackagePath}\n" +
            $"Backup belongs to: {sourceUser}\n" +
            $"Target account: {_shell.Session.RestoreTargetAccountDisplay}\n" +
            $"Target profile folder: {targetProfilePath}\n" +
            $"Account source: {(_shell.Session.RestoreUseExistingAccount ? "existing account on this computer" : "manually entered account")}\n" +
            $"Printers selected for restore: {_shell.Session.SelectedRestorePrinters.Count}\n" +
            "The restore will extract the selected profile content directly into that folder, repair access, reconnect the profile mapping, and then attempt to restore the selected printers." +
            (isCrossUserRestore
                ? "\n\nSafety note: this backup belongs to a different user than the target account. Overwrite is disabled for cross-user restores."
                : string.Empty) +
            (!targetProfileExists
                ? "\n\nProfile note: no existing target profile folder was found. Merge requires a healthy profile that already exists; use overwrite for a same-user rebuild, or sign into the target account once before doing a merge."
                : string.Empty);

        MergeRadioButton.IsChecked = _shell.Session.RestoreCollisionMode == RestoreCollisionMode.MergeIntoExistingProfile;
        OverwriteRadioButton.IsChecked = _shell.Session.RestoreCollisionMode == RestoreCollisionMode.OverwriteExistingProfile;

        if (isCrossUserRestore)
        {
            OverwriteRadioButton.IsEnabled = false;
            MergeRadioButton.IsChecked = true;
            _shell.Session.RestoreCollisionMode = RestoreCollisionMode.MergeIntoExistingProfile;
        }

        if (!targetProfileExists)
        {
            MergeRadioButton.IsEnabled = false;
            MergeRadioButton.Content = "Merge into an existing profile (requires an existing profile folder)";

            if (isCrossUserRestore)
            {
                BeginButton.IsEnabled = false;
            }
        }
    }

    private void CollisionMode_Checked(object sender, RoutedEventArgs e)
    {
        _shell.Session.RestoreCollisionMode = OverwriteRadioButton.IsChecked == true
            ? RestoreCollisionMode.OverwriteExistingProfile
            : RestoreCollisionMode.MergeIntoExistingProfile;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToRestoreSelectPrintersPage();
    }

    private void Begin_Click(object sender, RoutedEventArgs e)
    {
        _shell.Session.RestoreStartedAt = DateTime.Now;
        _shell.Session.RestoreCompletedAt = null;
        _shell.Session.RestoreJobId = Guid.NewGuid().ToString("N");
        _shell.GoToRestoreProgressPage();
    }

    private static string GetProfilesRootPath()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return System.IO.Path.Combine(string.IsNullOrWhiteSpace(systemDrive) ? @"C:" : systemDrive, "Users");
    }

    private static string? GetComparableAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return null;
        }

        var trimmed = accountName.Trim();
        var slashIndex = trimmed.LastIndexOf('\\');
        if (slashIndex >= 0 && slashIndex < trimmed.Length - 1)
        {
            trimmed = trimmed[(slashIndex + 1)..];
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 0)
        {
            trimmed = trimmed[..atIndex];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
