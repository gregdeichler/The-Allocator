using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class BackupService
{
    private static readonly EnumerationOptions ProfileEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

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
        var telemetryLogPath = Path.Combine(backupRoot, "logs", $"{profile.UserName}-backup.telemetry.jsonl");
        var telemetry = new TelemetryService(
            TelemetryService.GetTelemetryRootForBackup(session.BackupDestinationFolder),
            telemetryLogPath,
            new TelemetryContext
            {
                JobId = string.IsNullOrWhiteSpace(session.BackupJobId) ? Guid.NewGuid().ToString("N") : session.BackupJobId,
                Operation = "backup",
                UserProfile = profile.UserName,
                SourceComputer = Environment.MachineName,
                BackupPath = archivePath,
                SourceOperatingSystem = MachineInfoService.GetOperatingSystemDisplayName()
            });
        var metadataTempRoot = Path.Combine(Path.GetTempPath(), $"allocator-backup-meta-{Guid.NewGuid():N}");
        var tempMetadataPath = Path.Combine(metadataTempRoot, session.BackupMetadataFileName);
        var tempPrintersPath = Path.Combine(metadataTempRoot, session.BackupPrintersFileName);

        var includedPaths = new List<string>();
        var excludedPaths = new List<string>();
        var copiedFileCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var filesSkipped = 0;
        var currentPhase = "prepare";

        try
        {
            Directory.CreateDirectory(backupRoot);
            Directory.CreateDirectory(metadataTempRoot);
            await telemetry.FlushAsync(cancellationToken);

            progressProxy.Report($"Preparing backup for {profile.DisplayName}...");
            messages.Add($"Backup started at {DateTime.Now:u}");
            telemetry.WriteInfo("Backup started.", phase: currentPhase, status: "started", path: archivePath);

            includedPaths.AddRange(GetIncludedRelativePaths(profile.ProfilePath, excludedPaths, messages, telemetry, out copiedFileCount, out filesSkipped));

            var printerDetails = PrinterDiscoveryService.GetPrinterDetails(session.SelectedBackupPrinters);
            var printersJson = JsonSerializer.Serialize(printerDetails, JsonOptions);
            await File.WriteAllTextAsync(printersPath, printersJson, cancellationToken);
            await File.WriteAllTextAsync(tempPrintersPath, printersJson, cancellationToken);

            messages.Add($"Captured {printerDetails.Count} selected printers.");
            telemetry.WriteInfo($"Captured {printerDetails.Count} selected printers.", phase: currentPhase, status: "running", filesCopied: copiedFileCount, filesSkipped: filesSkipped);

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

            currentPhase = "archive";
            progressProxy.Report("Phase 1 of 3: Creating archive...");
            telemetry.WriteInfo("Creating backup archive.", phase: currentPhase, status: "running", path: archivePath);
            var archiveExitCode = await SevenZipService.CreateArchiveFromPathsAsync(
                archivePath,
                profile.ProfilePath,
                includedPaths,
                GetExcludeArguments(),
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(archiveExitCode))
            {
                errorCount++;
                telemetry.WriteError($"7-Zip failed during archive creation with exit code {archiveExitCode}.", phase: currentPhase, status: "failed", errorCode: archiveExitCode.ToString(), path: archivePath);
                await telemetry.FlushAsync(cancellationToken);
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip failed with exit code {archiveExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, archiveExitCode, "archive creation");
            if (archiveExitCode == 1)
            {
                warningCount++;
                telemetry.WriteWarning("7-Zip reported warnings during archive creation, but the backup continued.", phase: currentPhase, status: "warning", errorCode: archiveExitCode.ToString(), path: archivePath);
            }

            currentPhase = "details";
            progressProxy.Report("Phase 2 of 3: Adding backup details...");
            telemetry.WriteInfo("Adding backup details to the archive.", phase: currentPhase, status: "running", path: archivePath);
            var metadataArchiveExitCode = await SevenZipService.AddFilesToArchiveAsync(
                archivePath,
                metadataTempRoot,
                [session.BackupMetadataFileName, session.BackupPrintersFileName],
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(metadataArchiveExitCode))
            {
                errorCount++;
                telemetry.WriteError($"7-Zip could not add backup details to the archive. Exit code {metadataArchiveExitCode}.", phase: currentPhase, status: "failed", errorCode: metadataArchiveExitCode.ToString(), path: archivePath);
                await telemetry.FlushAsync(cancellationToken);
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip could not add backup details to the archive. Exit code {metadataArchiveExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, metadataArchiveExitCode, "adding backup details");
            if (metadataArchiveExitCode == 1)
            {
                warningCount++;
                telemetry.WriteWarning("7-Zip reported warnings while adding backup details, but the backup continued.", phase: currentPhase, status: "warning", errorCode: metadataArchiveExitCode.ToString(), path: archivePath);
            }

            manifest.ArchiveSizeBytes = new FileInfo(archivePath).Length;
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(tempMetadataPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

            currentPhase = "finalize";
            progressProxy.Report("Phase 3 of 3: Finalizing package...");
            telemetry.WriteInfo("Finalizing the backup package.", phase: currentPhase, status: "running", path: archivePath);
            var manifestUpdateExitCode = await SevenZipService.AddFilesToArchiveAsync(
                archivePath,
                metadataTempRoot,
                [session.BackupMetadataFileName],
                progressProxy,
                cancellationToken);
            if (!IsAcceptableSevenZipExitCode(manifestUpdateExitCode))
            {
                errorCount++;
                telemetry.WriteError($"7-Zip could not update the backup metadata inside the archive. Exit code {manifestUpdateExitCode}.", phase: currentPhase, status: "failed", errorCode: manifestUpdateExitCode.ToString(), path: archivePath);
                await telemetry.FlushAsync(cancellationToken);
                await File.WriteAllLinesAsync(logPath, messages, cancellationToken);
                return new BackupResult
                {
                    ErrorMessage = $"7-Zip could not update the backup metadata inside the archive. Exit code {manifestUpdateExitCode}.",
                    LogPath = logPath,
                    Messages = messages
                };
            }

            AddSevenZipExitCodeMessage(messages, manifestUpdateExitCode, "updating backup metadata");
            if (manifestUpdateExitCode == 1)
            {
                warningCount++;
                telemetry.WriteWarning("7-Zip reported warnings while finalizing backup metadata, but the backup continued.", phase: currentPhase, status: "warning", errorCode: manifestUpdateExitCode.ToString(), path: archivePath);
            }

            messages.Add($"Archive created at {archivePath}");
            messages.Add($"Archive size: {SizeFormattingService.ToReadableSize(manifest.ArchiveSizeBytes)}");
            messages.Add($"Archived files: {copiedFileCount:N0}");
            messages.Add("The archive was created directly from the source profile without duplicating the full profile onto the backup drive.");
            telemetry.WriteInfo(
                "Backup completed successfully.",
                phase: "complete",
                status: "success",
                path: archivePath,
                durationSeconds: session.BackupStartedAt.HasValue ? (DateTime.Now - session.BackupStartedAt.Value).TotalSeconds : null,
                filesCopied: copiedFileCount,
                filesSkipped: filesSkipped,
                bytesCopied: manifest.ArchiveSizeBytes,
                warningCount: warningCount,
                errorCount: errorCount);
            await telemetry.FlushAsync(cancellationToken);

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
            errorCount++;
            telemetry.WriteError(
                $"Backup failed: {ex.Message}",
                phase: currentPhase,
                status: "failed",
                exception: ex,
                path: archivePath);
            try
            {
                await telemetry.FlushAsync(cancellationToken);
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
        List<string> messages,
        TelemetryService telemetry,
        ref int filesSkipped)
    {
        var fileCount = 0;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", ProfileEnumerationOptions))
            {
                var fileName = Path.GetFileName(filePath);
                if (ShouldExcludeFile(fileName))
                {
                    excludedPaths.Add(filePath);
                    filesSkipped++;
                    continue;
                }

                fileCount++;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(sourcePath, "*", ProfileEnumerationOptions))
            {
                var directoryName = Path.GetFileName(childDirectory);
                if (ShouldExcludeDirectory(childDirectory, directoryName))
                {
                    excludedPaths.Add(childDirectory);
                    filesSkipped++;
                    continue;
                }

                fileCount += CountIncludedFiles(childDirectory, excludedPaths, messages, telemetry, ref filesSkipped);
            }
        }
        catch (UnauthorizedAccessException)
        {
            messages.Add($"Skipped inaccessible folder during backup scan: {sourcePath}");
            telemetry.WriteWarning("Skipped inaccessible folder during backup scan.", phase: "prepare", status: "warning", path: sourcePath);
            filesSkipped++;
        }
        catch (IOException ex)
        {
            messages.Add($"Skipped unreadable folder during backup scan: {sourcePath} ({ex.Message})");
            telemetry.WriteWarning($"Skipped unreadable folder during backup scan: {ex.Message}", phase: "prepare", status: "warning", path: sourcePath);
            filesSkipped++;
        }

        return fileCount;
    }

    private static List<string> GetIncludedRelativePaths(
        string profilePath,
        List<string> excludedPaths,
        List<string> messages,
        TelemetryService telemetry,
        out int copiedFileCount,
        out int filesSkipped)
    {
        var includedPaths = new List<string>();
        copiedFileCount = 0;
        filesSkipped = 0;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(profilePath, "*", ProfileEnumerationOptions))
            {
                var fileName = Path.GetFileName(filePath);
                if (ShouldExcludeFile(fileName))
                {
                    excludedPaths.Add(filePath);
                    filesSkipped++;
                    continue;
                }

                includedPaths.Add(fileName);
                copiedFileCount++;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(profilePath, "*", ProfileEnumerationOptions))
            {
                var directoryName = Path.GetFileName(directoryPath);
                if (ShouldExcludeDirectory(directoryPath, directoryName))
                {
                    excludedPaths.Add(directoryPath);
                    filesSkipped++;
                    continue;
                }

                includedPaths.Add(directoryName);
                copiedFileCount += CountIncludedFiles(directoryPath, excludedPaths, messages, telemetry, ref filesSkipped);
            }
        }
        catch (UnauthorizedAccessException)
        {
            messages.Add($"Skipped inaccessible content while scanning {profilePath}.");
            telemetry.WriteWarning("Skipped inaccessible content while scanning the profile root.", phase: "prepare", status: "warning", path: profilePath);
            filesSkipped++;
        }
        catch (IOException ex)
        {
            messages.Add($"Skipped unreadable content while scanning {profilePath}: {ex.Message}");
            telemetry.WriteWarning($"Skipped unreadable content while scanning the profile root: {ex.Message}", phase: "prepare", status: "warning", path: profilePath);
            filesSkipped++;
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
