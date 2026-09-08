using System.IO;

namespace TheAllocator.Services;

public static class PortableProfilePolicy
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        "Temp",
        "Application Data",
        "History",
        "INetCache",
        "Temporary Internet Files",
        "CrashDumps",
        "D3DSCache",
        "Cache",
        "Caches",
        "Code Cache",
        "GPUCache",
        "Service Worker",
        "WindowsApps",
        "My Music",
        "My Pictures",
        "My Videos"
    ];

    private static readonly string[] SensitiveRoamingPaths =
    [
        Path.Combine("AppData", "Roaming", "Microsoft", "Credentials"),
        Path.Combine("AppData", "Roaming", "Microsoft", "Crypto"),
        Path.Combine("AppData", "Roaming", "Microsoft", "Protect"),
        Path.Combine("AppData", "Roaming", "Microsoft", "SystemCertificates"),
        Path.Combine("AppData", "Roaming", "Microsoft", "Vault"),
        Path.Combine("AppData", "Roaming", "Microsoft", "Windows")
    ];

    public static bool ShouldExcludeFile(string fileName) =>
        fileName.StartsWith("NTUSER.", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("ntuser.ini", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("UsrClass.dat", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".search-ms", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldExcludeDirectory(string profileRoot, string fullPath, string directoryName)
    {
        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        if (ExcludedDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(profileRoot, fullPath);
        return IsMachineSpecificPath(relativePath);
    }

    public static IReadOnlyList<string> GetBackupExcludeArguments()
    {
        var arguments = ExcludedDirectoryNames
            .Select(directoryName => $"-xr!{directoryName}")
            .ToList();

        foreach (var pattern in GetRestoreExcludePatterns())
        {
            arguments.Add($"-xr!{pattern}");
        }

        arguments.Add("-x!*.search-ms");
        return arguments;
    }

    public static string[] GetRestoreExcludePatterns() =>
    [
        "NTUSER.*",
        "ntuser.ini",
        "UsrClass.dat*",
        Path.Combine("AppData", "Local"),
        Path.Combine("AppData", "LocalLow"),
        .. SensitiveRoamingPaths
    ];

    public static bool IsMachineSpecificPath(string relativePath) =>
        relativePath.Equals(Path.Combine("AppData", "Local"), StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(Path.Combine("AppData", "Local") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        relativePath.Equals(Path.Combine("AppData", "LocalLow"), StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(Path.Combine("AppData", "LocalLow") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        SensitiveRoamingPaths.Any(path =>
            relativePath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
}
