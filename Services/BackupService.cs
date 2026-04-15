using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class BackupService
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        "Temp",
        "INetCache",
        "Temporary Internet Files",
        "CrashDumps",
        "D3DSCache",
        "Cache",
        "Caches",
        "Code Cache",
        "GPUCache",
        "Service Worker",
        "My Music",
        "My Pictures",
        "My Videos"
    ];

    private static readonly string[] ExcludedFilePatterns =
    [
        "NTUSER.DAT.LOG",
        "UsrClass.dat.LOG"
    ];

    private static readonly string[] ExcludedFileExtensions =
    [
        ".search-ms"
    ];

    public BackupService(PrinterDiscoveryService printerDiscoveryService, SevenZipService sevenZipService)
    {
        PrinterDiscoveryService = printerDiscoveryService;
        SevenZipService = sevenZipService;
    }

    public PrinterDiscoveryService PrinterDiscoveryService { get; }

    public SevenZipService SevenZipService { get; }

    public async Task<BackupResult> CreateBackupAsync(
        AllocatorSession session,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        IProgress<string> progressProxy = new Progress<string>(message =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                messages.Add(message);
                progress?.Report(message);
            }
        });

        if (session.SelectedBackupProfile is null)
        {
            return new BackupResult { ErrorMessage = "No backup profile was selected." };
        }

        if (string.IsNullOrWhiteSpace(session.BackupDestinationFolder))
        {
            return new BackupResult { ErrorMessage = "No backup destination folder was selected." };
        }

        if (!SevenZipService.IsAvailable)
        {
            return new BackupResult { ErrorMessage = "The integrated 7-Zip engine is missing." };
        }

        var profile = session.SelectedBackupProfile;
        var backupRoot = Path.Combine(session.BackupDestinationFolder, GetBackupFolderName(profile.UserName));
        var archivePath = Path.Combine(backupRoot, session.BackupPackageName);
        var metadataPath = Path.Combine(backupRoot, session.BackupMetadataFileName);
        var printersPath = Path.Combine(backupRoot, session.BackupPrintersFileName);
        var logPath = Path.Combine(backupRoot, session.BackupLogFileName);
        var metadataTempRoot = Path.Combine(Path.GetTempPath(), $"allocator-backup-meta-{Guid.NewGuid():N}");
        var tempMetadataPath = Path.Combine(metadataTempRoot, session.BackupMetadataFileName);
        var tempPrintersPath = Path.Combine(metadataTempRoot, session.BackupPrintersFileName);

        var includedPaths = new List<string>();
        var excludedPaths = new List<string>();
        var copiedFileCount = 0;

        try
        {
            Directory.CreateDirectory(backupRoot);
            Directory.CreateDirectory(metadataTempRoot);

            progressProxy.Report($"Preparing backup for {profile.DisplayName}...");
            messages.Add($"Backup started at {DateTime.Now:u}");

            includedPaths.AddRange(GetIncludedRelativePaths(profile.ProfilePath, excludedPaths, messages, out copiedFileCount));

            var printerDetails = PrinterDiscoveryService.GetPrinterDetails(session.SelectedBackupPrinters);
            var printersJson = JsonSerializer.Serialize(printerDetails, JsonOptions);
            await File.WriteAllTextAsync(printersPath, printersJson, cancellationToken);
            await File.WriteAllTextAsync(tempPrintersPath, printersJson, cancellationToken);

            messages.Add($"Captured {printerDetails.Count} selected printers.");

            var manifest = new BackupManifest
            {
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0",
                CreatedAt = DateTime.Now,
                SourceComputerName = Environment.MachineName,
                SourceOperatingSystem = MachineInfoService.GetOperatingSystemDisplayName(),
                SourceOperatingSystemVersion = MachineInfoService.GetOperatingSystemVersionValue(),
                UserName = profile.UserName,
                DisplayName = profile.DisplayName,
                Sid = profile.Sid,
                ProfilePath = profile.ProfilePath,
                IsDomainLinked = IsDomainLinked(profile.Sid),
                ArchiveFileName = Path.GetFileName(archivePath),
                ArchiveSizeBytes = 0,
                MetadataFileName = session.BackupMetadataFileName,
                PrintersFileName = session.BackupPrintersFileName,
                LogFileName = session.BackupLogFileName,
                IncludedPaths = includedPaths,
                ExcludedPaths = excludedPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToList()
            };

            await File.WriteAllTextAsync(tempMetadataPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

            progressProxy.Report("Phase 1 of 3: Creating archive...");
            var archiveExitCode = await SevenZipService.CreateArchiveFromPathsAsync(
                archivePath,
                profile.ProfilePath,
                includedPaths,
                GetExcludeArguments(),
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(archiveExitCode))
            {
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip failed with exit code {archiveExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, archiveExitCode, "archive creation");

            progressProxy.Report("Phase 2 of 3: Adding backup details...");
            var metadataArchiveExitCode = await SevenZipService.AddFilesToArchiveAsync(
                archivePath,
                metadataTempRoot,
                [session.BackupMetadataFileName, session.BackupPrintersFileName],
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(metadataArchiveExitCode))
            {
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip could not add backup details to the archive. Exit code {metadataArchiveExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, metadataArchiveExitCode, "adding backup details");

            manifest.ArchiveSizeBytes = new FileInfo(archivePath).Length;
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(tempMetadataPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

            progressProxy.Report("Phase 3 of 3: Finalizing package...");
            var manifestUpdateExitCode = await SevenZipService.AddFilesToArchiveAsync(
                archivePath,
                metadataTempRoot,
                [session.BackupMetadataFileName],
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(manifestUpdateExitCode))
            {
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip could not update the backup metadata inside the archive. Exit code {manifestUpdateExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, manifestUpdateExitCode, "updating backup metadata");

            messages.Add($"Archive created at {archivePath}");
            messages.Add($"Archive size: {SizeFormattingService.ToReadableSize(manifest.ArchiveSizeBytes)}");
            messages.Add($"Archived files: {copiedFileCount:N0}");
            messages.Add("The archive was created directly from the source profile without duplicating the full profile onto the backup drive.");

            await File.WriteAllLinesAsync(logPath, messages, cancellationToken);

            return new BackupResult
            {
                Success = true,
                ArchivePath = archivePath,
                MetadataPath = metadataPath,
                PrintersPath = printersPath,
                LogPath = logPath,
                ArchiveSizeBytes = manifest.ArchiveSizeBytes,
                CopiedFileCount = copiedFileCount,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            messages.Add($"Backup failed: {ex.Message}");
            try
            {
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
            }
            catch
            {
            }

            return new BackupResult
            {
                ErrorMessage = ex.Message,
                Messages = messages
            };
        }
        finally
        {
            SafeDeleteDirectory(metadataTempRoot, messages);
        }
    }

    private static int CountIncludedFiles(
        string sourcePath,
        List<string> excludedPaths,
        List<string> messages)
    {
        var fileCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(sourcePath))
        {
            var fileName = Path.GetFileName(filePath);
            if (ShouldExcludeFile(fileName))
            {
                excludedPaths.Add(filePath);
                continue;
            }

            try
            {
                fileCount++;
            }
            catch (Exception ex)
            {
                messages.Add($"Skipped file count for {filePath}: {ex.Message}");
            }
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourcePath))
        {
            var directoryName = Path.GetFileName(childDirectory);
            if (ShouldExcludeDirectory(childDirectory, directoryName))
            {
                excludedPaths.Add(childDirectory);
                continue;
            }

            fileCount += CountIncludedFiles(childDirectory, excludedPaths, messages);
        }

        return fileCount;
    }

    private static List<string> GetIncludedRelativePaths(
        string profilePath,
        List<string> excludedPaths,
        List<string> messages,
        out int copiedFileCount)
    {
        var includedPaths = new List<string>();
        copiedFileCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(profilePath))
        {
            var fileName = Path.GetFileName(filePath);
            if (ShouldExcludeFile(fileName))
            {
                excludedPaths.Add(filePath);
                continue;
            }

            includedPaths.Add(fileName);
            copiedFileCount++;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(profilePath))
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (ShouldExcludeDirectory(directoryPath, directoryName))
            {
                excludedPaths.Add(directoryPath);
                continue;
            }

            includedPaths.Add(directoryName);
            copiedFileCount += CountIncludedFiles(directoryPath, excludedPaths, messages);
        }

        return includedPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetExcludeArguments()
    {
        var arguments = new List<string>();

        foreach (var directoryName in ExcludedDirectoryNames)
        {
            arguments.Add($"-xr!{directoryName}");
        }

        foreach (var pattern in ExcludedFilePatterns)
        {
            arguments.Add($"-x!{pattern}*");
        }

        foreach (var extension in ExcludedFileExtensions)
        {
            arguments.Add($"-x!*{extension}");
        }

        arguments.Add(@"-xr!LocalCache");
        return arguments;
    }

    private static bool ShouldExcludeDirectory(string fullPath, string directoryName)
    {
        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
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

        return fullPath.Contains(@"\Packages\", StringComparison.OrdinalIgnoreCase)
            && fullPath.EndsWith(@"\LocalCache", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExcludeFile(string fileName) =>
        ExcludedFilePatterns.Any(pattern => fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) ||
        ExcludedFileExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsAcceptableSevenZipExitCode(int exitCode) => exitCode is 0 or 1;

    private static string GetBackupFolderName(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();

    private static void AddSevenZipExitCodeMessage(List<string> messages, int exitCode, string operation)
    {
        if (exitCode == 1)
        {
            messages.Add($"7-Zip reported warnings during {operation}, but the backup continued.");
        }
    }

    private static void SafeDeleteDirectory(string directoryPath, List<string> messages)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            foreach (var subDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                try
                {
                    File.SetAttributes(subDirectory, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            messages.Add($"Could not fully clean staging folder {directoryPath}: {ex.Message}");
        }
    }

    private static bool IsDomainLinked(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }

        try
        {
            var sidValue = new SecurityIdentifier(sid);
            return sidValue.AccountDomainSid is not null;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
