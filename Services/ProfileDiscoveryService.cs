using System.IO;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class ProfileDiscoveryService
{
    private static readonly HashSet<string> ExcludedProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "All Users",
        "Default",
        "Default User",
        "defaultuser0",
        "Public",
        "WDAGUtilityAccount",
        "usadmin"
    };

    public IReadOnlyList<ProfileOption> GetProfiles()
    {
        const string usersRoot = @"C:\Users";
        if (!Directory.Exists(usersRoot))
        {
            return [];
        }

        var sidMap = ReadSidMap();

        return Directory
            .EnumerateDirectories(usersRoot)
            .Select(path => BuildProfile(path, sidMap))
            .Where(profile => profile is not null)
            .Cast<ProfileOption>()
            .OrderBy(profile => profile.DisplayName)
            .ToList();
    }

    private static ProfileOption? BuildProfile(string profilePath, IReadOnlyDictionary<string, string> sidMap)
    {
        var userName = Path.GetFileName(profilePath);
        if (string.IsNullOrWhiteSpace(userName) || ExcludedProfiles.Contains(userName))
        {
            return null;
        }

        return new ProfileOption
        {
            UserName = userName,
            DisplayName = userName,
            ProfilePath = profilePath,
            Sid = sidMap.TryGetValue(profilePath, out var sid) ? sid : string.Empty
        };
    }

    private static Dictionary<string, string> ReadSidMap()
    {
        var sidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var baseKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (baseKey is null)
        {
            return sidMap;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName);
            var path = subKey?.GetValue("ProfileImagePath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                sidMap[path] = subKeyName;
            }
        }

        return sidMap;
    }
}
