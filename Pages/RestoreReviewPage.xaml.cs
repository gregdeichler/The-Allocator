using System.IO;
using System.Windows;
using System.Windows.Controls;
using TheAllocator.Models;
using TheAllocator.Services;

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
        var isSameUserRestore = !isCrossUserRestore;
        var isLegacySource = IsLegacySourceRestore();
        var isCrossBuildRestore = isSameUserRestore &&
                                  IsCrossWindowsBuild(
                                      _shell.Session.RestoreManifest?.SourceOperatingSystemVersion,
                                      MachineInfoService.GetOperatingSystemVersionValue());
        var accountReadiness = GetAccountReadiness();

        if (isCrossUserRestore)
        {
            _shell.Session.RestoreCollisionMode = RestoreCollisionMode.MergeIntoExistingProfile;
        }
        else
        {
            _shell.Session.RestoreCollisionMode = RestoreCollisionMode.OverwriteExistingProfile;
        }

        SummaryText.Text =
            $"Backup package: {_shell.Session.RestorePackagePath}\n" +
            $"Backup belongs to: {sourceUser}\n" +
            $"Target account: {_shell.Session.RestoreTargetAccountDisplay}\n" +
            $"Target profile folder: {targetProfilePath}\n" +
            $"Source Windows: {GetWindowsDisplay(_shell.Session.RestoreManifest?.SourceOperatingSystem, _shell.Session.RestoreManifest?.SourceOperatingSystemVersion)}\n" +
            $"This PC Windows: {GetWindowsDisplay(MachineInfoService.GetOperatingSystemDisplayName(), MachineInfoService.GetOperatingSystemVersionValue())}\n" +
            $"Target account check: {accountReadiness.Message}\n" +
            $"Account source: {(_shell.Session.RestoreUseExistingAccount ? "existing account on this computer" : "manually entered account")}\n" +
            $"Printers selected for restore: {_shell.Session.SelectedRestorePrinters.Count}\n" +
            "Profile hives: destination Windows-created state will be kept\n" +
            "Windows shell state: destination Windows-created state will be kept\n" +
            "Application data: portable Roaming settings only; machine-specific Local state will be skipped\n\n" +
            "Windows will create or validate the target profile first. The Allocator will then restore portable user content, repair access to migrated files, and attempt to recreate selected printers." +
            GetCompatibilityNote(isSameUserRestore, isLegacySource, isCrossBuildRestore) +
            (isCrossUserRestore
                ? "\n\nSafety note: this backup belongs to a different user than the target account. Fresh profile folder restore is disabled for cross-user restores."
                : string.Empty) +
            (!targetProfileExists
                ? "\n\nProfile note: no existing target profile folder was found. Restoring into an existing working profile requires the target user to sign in successfully first."
                : string.Empty);

        RecommendationText.Text = GetRecommendationText(isCrossUserRestore, targetProfileExists, isLegacySource, isCrossBuildRestore);

        if (!accountReadiness.Success)
        {
            BeginButton.IsEnabled = false;
            RecommendationText.Text = $"Blocked: {accountReadiness.Message}";
        }

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
            MergeRadioButton.Content = "Restore files into an existing working profile (requires an existing profile folder)";

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
        var manifestJobId = _shell.Session.RestoreManifest?.JobId;
        _shell.Session.RestoreJobId = string.IsNullOrWhiteSpace(manifestJobId)
            ? Guid.NewGuid().ToString("N")
            : manifestJobId;
        _shell.GoToRestoreProgressPage();
    }

    private static string GetProfilesRootPath()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return System.IO.Path.Combine(string.IsNullOrWhiteSpace(systemDrive) ? @"C:" : systemDrive, "Users");
    }

    private static string GetWindowsDisplay(string? name, string? version)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
        {
            return "Unknown";
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return name ?? "Unknown";
        }

        return string.IsNullOrWhiteSpace(name) ? version : $"{name} ({version})";
    }

    private static string GetRecommendationText(
        bool isCrossUserRestore,
        bool targetProfileExists,
        bool isLegacySource,
        bool isCrossBuildRestore)
    {
        if (isCrossUserRestore)
        {
            return targetProfileExists
                ? "Required: restore portable files into the existing target user's working profile."
                : "Blocked: cross-user restore needs an existing working target profile.";
        }

        if (!targetProfileExists)
        {
            return "Recommended: let Windows create a fresh profile for this user, then restore portable data.";
        }

        if (isLegacySource || isCrossBuildRestore)
        {
            return "Recommended: preserve this PC's Windows-created sign-in state and restore portable user data only.";
        }

        return "Recommended: preserve Windows-created sign-in state and restore portable user data.";
    }

    private string GetCompatibilityNote(bool isSameUserRestore, bool isLegacySource, bool isCrossBuildRestore)
    {
        var sourceVersion = _shell.Session.RestoreManifest?.SourceOperatingSystemVersion;
        var targetVersion = MachineInfoService.GetOperatingSystemVersionValue();

        if (isLegacySource)
        {
            return "\n\nCompatibility note: the source backup is from an older Windows release. Windows-owned profile and shell state will not be copied from the old computer.";
        }

        if (!isSameUserRestore || !isCrossBuildRestore)
        {
            return string.Empty;
        }

        var sourceBuild = GetWindowsBuild(sourceVersion) ?? sourceVersion ?? "unknown";
        var targetBuild = GetWindowsBuild(targetVersion) ?? targetVersion ?? "unknown";
        return $"\n\nCompatibility note: source Windows build {sourceBuild} differs from this PC's build {targetBuild}. Windows-owned profile, shell, and machine-specific application state will remain from this PC.";
    }

    private (bool Success, string Message) GetAccountReadiness()
    {
        try
        {
            var identity = _shell.WindowsProfileService.ResolveRestoreIdentity(_shell.Session);
            return (true, $"resolved successfully ({identity.Sid})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private bool IsLegacySourceRestore()
    {
        var versionValue = _shell.Session.RestoreManifest?.SourceOperatingSystemVersion;
        if (Version.TryParse(versionValue, out var parsedVersion))
        {
            return parsedVersion.Major < 10;
        }

        var sourceOperatingSystem = _shell.Session.RestoreManifest?.SourceOperatingSystem ?? string.Empty;
        return sourceOperatingSystem.Contains("Windows 7", StringComparison.OrdinalIgnoreCase) ||
               sourceOperatingSystem.Contains("Windows 8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCrossWindowsBuild(string? sourceVersion, string? targetVersion)
    {
        if (Version.TryParse(sourceVersion, out var parsedSourceVersion) &&
            Version.TryParse(targetVersion, out var parsedTargetVersion))
        {
            return parsedSourceVersion.Build != parsedTargetVersion.Build;
        }

        return false;
    }

    private static string? GetWindowsBuild(string? versionValue) =>
        Version.TryParse(versionValue, out var parsedVersion) && parsedVersion.Build >= 0
            ? parsedVersion.Build.ToString()
            : null;

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
