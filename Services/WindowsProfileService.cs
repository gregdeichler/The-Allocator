using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class WindowsProfileService
{
    private const string ProfileListRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const int ErrorAlreadyExists = 183;

    public WindowsProfileIdentity ResolveRestoreIdentity(AllocatorSession session)
    {
        var accountName = session.RestoreUseDomainAccount
            ? session.RestoreTargetAccountDisplay
            : $@"{Environment.MachineName}\{session.RestoreTargetUser}";

        var selectedProfileSid = session.SelectedRestoreExistingProfile?.Sid;
        if (session.RestoreUseExistingAccount && !string.IsNullOrWhiteSpace(selectedProfileSid))
        {
            return CreateIdentityFromSid(accountName, session.RestoreTargetUser, selectedProfileSid);
        }

        var sourceUser = GetComparableAccountName(session.RestoreManifest?.UserName);
        var targetUser = GetComparableAccountName(session.RestoreTargetUser);
        var sameDomainUser = session.RestoreManifest?.IsDomainLinked == true &&
                             session.RestoreUseDomainAccount &&
                             !string.IsNullOrWhiteSpace(sourceUser) &&
                             string.Equals(sourceUser, targetUser, StringComparison.OrdinalIgnoreCase);

        if (sameDomainUser && !string.IsNullOrWhiteSpace(session.RestoreManifest?.Sid))
        {
            return CreateIdentityFromSid(accountName, session.RestoreTargetUser, session.RestoreManifest.Sid);
        }

        return ResolveIdentity(accountName, session.RestoreTargetUser);
    }

    public WindowsProfileIdentity ResolveIdentity(string accountName, string userName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException("No target Windows account was selected.");
        }

        try
        {
            var sid = (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            return new WindowsProfileIdentity(accountName, userName, sid.Value);
        }
        catch (IdentityNotMappedException)
        {
            throw new InvalidOperationException(
                $"Windows could not resolve the target account '{accountName}'. Confirm the account exists and, for a domain account, that this computer can reach the domain.");
        }
    }

    private static WindowsProfileIdentity CreateIdentityFromSid(string accountName, string userName, string sidValue)
    {
        try
        {
            var sid = new SecurityIdentifier(sidValue);
            return new WindowsProfileIdentity(accountName, userName, sid.Value);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"The saved Windows SID for {accountName} is not valid.");
        }
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
        return atIndex > 0 ? trimmed[..atIndex] : trimmed;
    }

    public string CreateFreshProfile(WindowsProfileIdentity identity, string expectedProfilePath)
    {
        EnsureProfileIsNotLoaded(identity);

        var registeredPath = GetRegisteredProfilePath(identity.Sid);
        if (!string.IsNullOrWhiteSpace(registeredPath))
        {
            if (!DeleteProfile(identity.Sid, registeredPath, null))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Windows could not remove the existing profile for {identity.AccountName}.");
            }
        }
        else if (Directory.Exists(expectedProfilePath))
        {
            SafeDeleteUnregisteredProfileDirectory(expectedProfilePath);
        }

        RemoveStaleBackupRegistration(identity.Sid);

        var profilePath = new StringBuilder(512);
        var result = CreateProfile(identity.Sid, identity.UserName, profilePath, (uint)profilePath.Capacity);
        if (result != 0)
        {
            var win32Code = result & 0xFFFF;
            if (win32Code == ErrorAlreadyExists)
            {
                throw new InvalidOperationException(
                    $"Windows reports that a profile already exists for {identity.AccountName}. Remove the stale profile before retrying.");
            }

            Marshal.ThrowExceptionForHR(result);
        }

        var createdPath = profilePath.ToString();
        ValidateRegisteredProfile(identity, createdPath);
        return createdPath;
    }

    public string ValidateExistingProfile(WindowsProfileIdentity identity, string selectedProfilePath)
    {
        EnsureProfileIsNotLoaded(identity);

        var registeredPath = GetRegisteredProfilePath(identity.Sid);
        if (string.IsNullOrWhiteSpace(registeredPath))
        {
            throw new InvalidOperationException(
                $"Windows does not have a registered profile for {identity.AccountName}. Create a fresh profile instead.");
        }

        if (!PathsMatch(registeredPath, selectedProfilePath))
        {
            throw new InvalidOperationException(
                $"The selected folder does not match the profile Windows registered for {identity.AccountName}. Windows expects '{registeredPath}'.");
        }

        ValidateRegisteredProfile(identity, registeredPath);
        return registeredPath;
    }

    public void ValidateRegisteredProfile(WindowsProfileIdentity identity, string profilePath)
    {
        var registeredPath = GetRegisteredProfilePath(identity.Sid);
        if (string.IsNullOrWhiteSpace(registeredPath) || !PathsMatch(registeredPath, profilePath))
        {
            throw new InvalidOperationException(
                $"Windows did not register the restored profile correctly for {identity.AccountName}.");
        }

        if (!Directory.Exists(profilePath) || !File.Exists(Path.Combine(profilePath, "NTUSER.DAT")))
        {
            throw new InvalidOperationException(
                "Windows created an incomplete target profile. The restore was stopped before user data was applied.");
        }
    }

    public string? GetRegisteredProfilePath(string sid)
    {
        using var profileListKey = Registry.LocalMachine.OpenSubKey(ProfileListRegistryPath);
        using var profileKey = profileListKey?.OpenSubKey(sid);
        var path = profileKey?.GetValue("ProfileImagePath") as string;
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Environment.ExpandEnvironmentVariables(path);
    }

    private static void EnsureProfileIsNotLoaded(WindowsProfileIdentity identity)
    {
        using var loadedHive = Registry.Users.OpenSubKey(identity.Sid);
        if (loadedHive is not null)
        {
            throw new InvalidOperationException(
                $"The profile for {identity.AccountName} is currently loaded. Sign that user out completely, then retry from the technician account.");
        }
    }

    private static void SafeDeleteUnregisteredProfileDirectory(string profilePath)
    {
        var profilesRoot = Path.GetFullPath(Path.Combine(
            Environment.GetEnvironmentVariable("SystemDrive") ?? @"C:",
            "Users"));
        var resolvedPath = Path.GetFullPath(profilePath);
        var expectedPrefix = profilesRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!resolvedPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove an unexpected profile path: {resolvedPath}");
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var filePath in Directory.EnumerateFiles(resolvedPath, "*", enumerationOptions))
        {
            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        Directory.Delete(resolvedPath, recursive: true);
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void RemoveStaleBackupRegistration(string sid)
    {
        using var profileListKey = Registry.LocalMachine.OpenSubKey(ProfileListRegistryPath, writable: true);
        profileListKey?.DeleteSubKeyTree($"{sid}.bak", throwOnMissingSubKey: false);
    }

    [DllImport("userenv.dll", EntryPoint = "CreateProfile", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int CreateProfile(
        string pszUserSid,
        string pszUserName,
        StringBuilder pszProfilePath,
        uint cchProfilePath);

    [DllImport("userenv.dll", EntryPoint = "DeleteProfileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteProfile(
        string lpSidString,
        string? lpProfilePath,
        string? lpComputerName);
}

public sealed record WindowsProfileIdentity(string AccountName, string UserName, string Sid);
