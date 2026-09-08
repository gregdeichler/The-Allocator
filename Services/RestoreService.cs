using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class RestoreService
{
    public RestoreService(SevenZipService sevenZipService, WindowsProfileService windowsProfileService)
    {
        SevenZipService = sevenZipService;
        WindowsProfileService = windowsProfileService;
    }

    public SevenZipService SevenZipService { get; }

    public WindowsProfileService WindowsProfileService { get; }

    public async Task<RestorePackageInfo> InspectPackageAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return new RestorePackageInfo { ErrorMessage = "No backup package was selected." };
        }

        if (!File.Exists(archivePath))
        {
            return new RestorePackageInfo { ErrorMessage = "The selected backup package could not be found." };
        }

        var metadataPath = Path.ChangeExtension(archivePath, ".json");
        var printersPath = GetSidecarPrintersPath(archivePath);
        var tempExtractPath = Path.Combine(Path.GetTempPath(), $".allocator-inspect-{Guid.NewGuid():N}");

        try
        {
            BackupManifest? manifest = null;
            List<BackupPrinterInfo> printers = [];

            if (File.Exists(metadataPath))
            {
                manifest = await ReadJsonAsync<BackupManifest>(metadataPath, cancellationToken);
            }

            if (File.Exists(printersPath))
            {
                printers = await ReadJsonAsync<List<BackupPrinterInfo>>(printersPath, cancellationToken) ?? [];
            }

            if (manifest is null)
            {
                var extractCode = await SevenZipService.ExtractArchiveAsync(
                    archivePath,
                    tempExtractPath,
                    cancellationToken: cancellationToken,
                    includePatterns: ["*.json"]);

                if (!IsAcceptableSevenZipExitCode(extractCode))
                {
                    return new RestorePackageInfo
                    {
                        ErrorMessage = $"The backup package could not be inspected. 7-Zip returned exit code {extractCode}."
                    };
                }

                metadataPath = Directory.EnumerateFiles(tempExtractPath, "*-backup.json", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
                printersPath = Directory.EnumerateFiles(tempExtractPath, "*-printers.json", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(metadataPath))
                {
                    manifest = await ReadJsonAsync<BackupManifest>(metadataPath, cancellationToken);
                }

                if (manifest is not null && !File.Exists(printersPath))
                {
                    printersPath = Directory.EnumerateFiles(tempExtractPath, manifest.PrintersFileName, SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(printersPath))
                {
                    printers = await ReadJsonAsync<List<BackupPrinterInfo>>(printersPath, cancellationToken) ?? [];
                }
            }

            if (manifest is null)
            {
                return new RestorePackageInfo
                {
                    ErrorMessage = "The backup package did not contain readable allocator metadata."
                };
            }

            return new RestorePackageInfo
            {
                Success = true,
                Manifest = manifest,
                Printers = printers,
                MetadataPath = metadataPath,
                PrintersPath = printersPath
            };
        }
        catch (Exception ex)
        {
            return new RestorePackageInfo
            {
                ErrorMessage = $"The backup package could not be read: {ex.Message}"
            };
        }
        finally
        {
            SafeDeleteDirectory(tempExtractPath);
        }
    }

    public async Task<RestoreResult> RestoreAsync(
        AllocatorSession session,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (session.RestoreManifest is null)
        {
            return new RestoreResult { ErrorMessage = "No restore package metadata is loaded." };
        }

        if (string.IsNullOrWhiteSpace(session.RestorePackagePath) || !File.Exists(session.RestorePackagePath))
        {
            return new RestoreResult { ErrorMessage = "The restore package could not be found." };
        }

        var targetProfilePath = GetTargetProfilePath(session);
        var archiveDirectory = Path.GetDirectoryName(session.RestorePackagePath) ?? Path.GetTempPath();
        var restoreLogPath = Path.Combine(
            archiveDirectory,
            GetRestoreLogFileName(session.RestoreManifest.UserName));
        var telemetry = new TelemetryService(
            TelemetryService.GetTelemetryRootForRestore(session.RestorePackagePath),
            Path.Combine(archiveDirectory, "logs", $"{session.RestoreManifest.UserName}-restore.telemetry.jsonl"),
            new TelemetryContext
            {
                JobId = string.IsNullOrWhiteSpace(session.RestoreJobId) ? Guid.NewGuid().ToString("N") : session.RestoreJobId,
                Operation = "restore",
                UserProfile = session.RestoreManifest.UserName,
                SourceComputer = session.RestoreManifest.SourceComputerName,
                TargetComputer = Environment.MachineName,
                RestorePath = targetProfilePath,
                BackupPath = session.RestorePackagePath,
                SourceOperatingSystem = session.RestoreManifest.SourceOperatingSystem,
                TargetOperatingSystem = MachineInfoService.GetOperatingSystemDisplayName()
            });
        var logger = new RestoreLogger(restoreLogPath, telemetry);

        try
        {
            await telemetry.FlushAsync(cancellationToken);
            logger.Add($"Restore started at {DateTime.Now:u}");
            logger.Add($"Restore package: {session.RestorePackagePath}");
            logger.Add($"Target profile path: {targetProfilePath}");
            logger.Add($"Collision mode: {session.RestoreCollisionMode}");
            logger.Add($"Restore approach: {GetRestoreApproachLogText(session)}");
            logger.Add($"Source operating system: {session.RestoreManifest.SourceOperatingSystem} ({session.RestoreManifest.SourceOperatingSystemVersion})");
            logger.Add($"Target operating system: {MachineInfoService.GetOperatingSystemDisplayName()} ({MachineInfoService.GetOperatingSystemVersionValue()})");
            logger.Add($"Target profile existed before restore: {Directory.Exists(targetProfilePath)}");
            logger.Add($"Profile hives plan: {GetProfileHivePlanLogText(session)}");
            logger.Add($"Windows shell state plan: {GetShellStatePlanLogText(session)}");
            ValidateCrossUserOverwrite(session, logger);
            ValidateTargetProfileIsNotCurrentSignedInProfile(targetProfilePath, logger);
            var targetIdentity = WindowsProfileService.ResolveRestoreIdentity(session);
            logger.Add($"Resolved target account SID: {targetIdentity.Sid}");
            ValidateTargetIdentity(session, targetIdentity, logger);

            IProgress<string> progressProxy = new Progress<string>(message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    logger.Add(message);
                    progress?.Report(message);
                }
            });

            progressProxy.Report("Validating the backup archive and destination space...");
            var restorablePaths = session.RestoreManifest.IncludedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(ShouldRestoreProfilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var uncompressedSize = await SevenZipService.GetArchiveUncompressedSizeAsync(
                session.RestorePackagePath,
                cancellationToken,
                PortableProfilePolicy.GetRestoreExcludePatterns(),
                restorablePaths);
            ValidateDestinationSpace(targetProfilePath, uncompressedSize);
            logger.Add($"Archive file data size: {SizeFormattingService.ToReadableSize(uncompressedSize)}");
            logger.Add("Archive headers and destination free space passed preflight validation.");

            progressProxy.Report("Asking Windows to prepare the target profile...");
            targetProfilePath = PrepareTargetProfile(targetProfilePath, targetIdentity, session.RestoreCollisionMode, logger);

            progressProxy.Report("Preparing access for migrated content...");
            ApplyBasePermissions(targetProfilePath, targetIdentity, logger);

            progressProxy.Report("Extracting files directly into the target profile...");
            var copiedFileCount = await ExtractProfileContentDirectlyAsync(
                session.RestorePackagePath,
                session.RestoreManifest,
                session,
                targetProfilePath,
                progressProxy,
                logger,
                cancellationToken);

            progressProxy.Report("Validating the Windows profile registration...");
            WindowsProfileService.ValidateRegisteredProfile(targetIdentity, targetProfilePath);
            ValidateProfileAccess(targetProfilePath, targetIdentity);
            logger.Add("Windows profile registration and destination profile hive were validated.");

            progressProxy.Report("Restoring selected printers...");
            await RestorePrintersAsync(session, logger, cancellationToken);

            logger.Add($"Copied files: {copiedFileCount:N0}");
            logger.Add($"Portable data restore finished at {DateTime.Now:u}");
            logger.Add("Migration validation is pending until the restored user signs in after reboot.");
            telemetry.WriteInfo(
                "Portable data restore completed; user sign-in validation is pending.",
                phase: "complete",
                status: "completed",
                path: targetProfilePath,
                durationSeconds: session.RestoreStartedAt.HasValue ? (DateTime.Now - session.RestoreStartedAt.Value).TotalSeconds : null,
                filesCopied: copiedFileCount);
            await telemetry.FlushAsync(cancellationToken);

            return new RestoreResult
            {
                Success = true,
                RestoreLogPath = restoreLogPath,
                TargetProfilePath = targetProfilePath,
                CopiedFileCount = copiedFileCount,
                Messages = logger.Messages
            };
        }
        catch (Exception ex)
        {
            logger.Add($"Restore failed: {ex.Message}");
            telemetry.WriteError(
                $"Restore failed: {ex.Message}",
                phase: "restore",
                status: "failed",
                exception: ex,
                path: targetProfilePath);
            await telemetry.FlushAsync(cancellationToken);

            return new RestoreResult
            {
                ErrorMessage = ex.Message,
                RestoreLogPath = restoreLogPath,
                Messages = logger.Messages
            };
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static string GetSidecarPrintersPath(string archivePath)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? string.Empty;
        var archiveFileName = Path.GetFileNameWithoutExtension(archivePath);
        var userName = archiveFileName.EndsWith("-backup", StringComparison.OrdinalIgnoreCase)
            ? archiveFileName[..^"-backup".Length]
            : archiveFileName;

        return Path.Combine(directory, $"{userName}-printers.json");
    }

    private static string GetTargetProfilePath(AllocatorSession session)
    {
        if (session.RestoreUseExistingAccount && session.SelectedRestoreExistingProfile is not null)
        {
            return session.SelectedRestoreExistingProfile.ProfilePath;
        }

        return Path.Combine(GetProfilesRootPath(), session.RestoreTargetUser);
    }

    private static void ValidateTargetProfileIsNotCurrentSignedInProfile(string targetProfilePath, RestoreLogger logger)
    {
        var currentProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(currentProfilePath))
        {
            return;
        }

        if (!string.Equals(
                Path.GetFullPath(currentProfilePath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(targetProfilePath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        logger.Add("Restore was blocked because the target profile is the currently signed-in Windows profile.");
        throw new InvalidOperationException(
            "This restore is targeting the profile that is currently signed in. Sign in with a different local or admin account, then run the restore again.");
    }

    private static void ValidateDestinationSpace(string targetProfilePath, long uncompressedSize)
    {
        var rootPath = Path.GetPathRoot(Path.GetFullPath(targetProfilePath));
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("The destination Windows drive could not be determined.");
        }

        var drive = new DriveInfo(rootPath);
        var safetyMargin = Math.Max(5L * 1024 * 1024 * 1024, uncompressedSize / 20);
        var requiredSpace = checked(uncompressedSize + safetyMargin);
        if (drive.AvailableFreeSpace >= requiredSpace)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The Windows drive does not have enough free space. Restore requires approximately {SizeFormattingService.ToReadableSize(requiredSpace)}, but only {SizeFormattingService.ToReadableSize(drive.AvailableFreeSpace)} is available.");
    }

    private static void ValidateCrossUserOverwrite(AllocatorSession session, RestoreLogger logger)
    {
        var sourceUser = GetComparableAccountName(session.RestoreManifest?.UserName);
        var targetUser = GetComparableAccountName(session.RestoreTargetUser);
        if (string.IsNullOrWhiteSpace(sourceUser) || string.IsNullOrWhiteSpace(targetUser))
        {
            return;
        }

        if (session.RestoreCollisionMode != RestoreCollisionMode.OverwriteExistingProfile)
        {
            return;
        }

        if (session.RestoreManifest?.IsDomainLinked != session.RestoreUseDomainAccount)
        {
            logger.Add("Fresh profile restore was blocked because the source and target account types differ.");
            throw new InvalidOperationException(
                "A fresh profile restore requires the same account type as the backup. Choose the domain account for a domain backup, or restore portable files into an existing working profile.");
        }

        if (string.Equals(sourceUser, targetUser, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        logger.Add($"Cross-user overwrite restore was blocked. Backup user '{session.RestoreManifest?.UserName}' does not match target user '{session.RestoreTargetUser}'.");
        throw new InvalidOperationException(
            "A Windows-created fresh profile is only available when the backup and target account are the same user. For a different user, restore portable files into that user's existing working profile.");
    }

    private static void ValidateTargetIdentity(
        AllocatorSession session,
        WindowsProfileIdentity targetIdentity,
        RestoreLogger logger)
    {
        var sourceUser = GetComparableAccountName(session.RestoreManifest?.UserName);
        var targetUser = GetComparableAccountName(session.RestoreTargetUser);
        var sameUserName = !string.IsNullOrWhiteSpace(sourceUser) &&
                           string.Equals(sourceUser, targetUser, StringComparison.OrdinalIgnoreCase);

        if (!sameUserName ||
            session.RestoreManifest?.IsDomainLinked != true ||
            !session.RestoreUseDomainAccount ||
            string.IsNullOrWhiteSpace(session.RestoreManifest.Sid))
        {
            return;
        }

        if (string.Equals(session.RestoreManifest.Sid, targetIdentity.Sid, StringComparison.OrdinalIgnoreCase))
        {
            logger.Add("Target domain SID matches the SID captured in the backup.");
            return;
        }

        logger.Add($"Target SID mismatch. Backup SID: {session.RestoreManifest.Sid}; target SID: {targetIdentity.Sid}.");
        throw new InvalidOperationException(
            "The selected domain account does not match the account captured in this backup. The restore was stopped before changing the profile.");
    }

    private string PrepareTargetProfile(
        string targetProfilePath,
        WindowsProfileIdentity identity,
        RestoreCollisionMode collisionMode,
        RestoreLogger logger)
    {
        if (collisionMode == RestoreCollisionMode.OverwriteExistingProfile)
        {
            var createdPath = WindowsProfileService.CreateFreshProfile(identity, targetProfilePath);
            logger.Add($"Windows created and registered a fresh target profile: {createdPath}");
            return createdPath;
        }

        var validatedPath = WindowsProfileService.ValidateExistingProfile(identity, targetProfilePath);
        logger.Add($"Windows validated the existing working target profile: {validatedPath}");
        return validatedPath;
    }

    private async Task<int> ExtractProfileContentDirectlyAsync(
        string archivePath,
        BackupManifest manifest,
        AllocatorSession session,
        string targetProfilePath,
        IProgress<string>? progress,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetProfilePath);

        var includePatterns = manifest.IncludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(ShouldRestoreProfilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skippedPaths = manifest.IncludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !ShouldRestoreProfilePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var skippedPath in skippedPaths)
        {
            logger.Add($"Skipped sensitive profile state during restore: {skippedPath}");
        }

        logger.Add("Windows-owned profile hives and machine-specific AppData will not be restored.");

        var extractCode = await SevenZipService.ExtractArchiveAsync(
            archivePath,
            targetProfilePath,
            progress,
            cancellationToken,
            PortableProfilePolicy.GetRestoreExcludePatterns(),
            includePatterns);

        if (!IsAcceptableSevenZipExitCode(extractCode))
        {
            throw new InvalidOperationException($"The backup archive could not be extracted. 7-Zip returned exit code {extractCode}.");
        }

        if (extractCode == 1)
        {
            logger.Add("7-Zip completed with warnings during direct restore extraction, but the restore continued.");
        }

        return CountRestoredFiles(targetProfilePath, includePatterns, logger);
    }

    private static int CountRestoredFiles(string targetProfilePath, IEnumerable<string> includedPaths, RestoreLogger logger)
    {
        var restoredFiles = 0;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var relativePath in includedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.Combine(targetProfilePath, relativePath);
                if (File.Exists(fullPath))
                {
                    restoredFiles++;
                    continue;
                }

                if (Directory.Exists(fullPath))
                {
                    restoredFiles += Directory.EnumerateFiles(fullPath, "*", enumerationOptions).Count();
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception ex)
            {
                logger.Add($"Could not count restored content for {relativePath}: {ex.Message}");
            }
        }

        return restoredFiles;
    }

    private static void ApplyBasePermissions(
        string targetProfilePath,
        WindowsProfileIdentity targetIdentity,
        RestoreLogger logger)
    {
        var directory = new DirectoryInfo(targetProfilePath);
        var accessControl = directory.GetAccessControl();
        var targetSid = new SecurityIdentifier(targetIdentity.Sid);
        var accessRule = new FileSystemAccessRule(
            targetSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

        accessControl.SetAccessRule(accessRule);
        directory.SetAccessControl(accessControl);
        logger.Add($"Applied inheritable access for target SID {targetIdentity.Sid} before extracting migrated content.");
    }

    private static void ValidateProfileAccess(string targetProfilePath, WindowsProfileIdentity targetIdentity)
    {
        var targetSid = new SecurityIdentifier(targetIdentity.Sid);
        var accessRules = new DirectoryInfo(targetProfilePath)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        var hasAccess = accessRules
            .OfType<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference == targetSid &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);

        if (!hasAccess)
        {
            throw new InvalidOperationException("The target account access rule could not be verified on the restored profile.");
        }
    }

    private static async Task RestorePrintersAsync(
        AllocatorSession session,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var printer in session.SelectedRestorePrinters.Where(printer => printer.IsSelected))
        {
            var connectionPath = !string.IsNullOrWhiteSpace(printer.ConnectionPath)
                ? printer.ConnectionPath
                : printer.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
                    ? printer.Name
                    : string.Empty;

            if (!string.IsNullOrWhiteSpace(connectionPath) &&
                connectionPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            {
                await RunProcessAsync(
                    "rundll32.exe",
                    $"printui.dll,PrintUIEntry /ga /n \"{connectionPath}\"",
                    logger,
                    cancellationToken);

                if (printer.IsDefault)
                {
                    logger.Add($"Printer '{printer.Name}' was the source default. The restored user must set the default after first sign-in because Windows stores that choice per user.");
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(printer.PortName) && !string.IsNullOrWhiteSpace(printer.DriverName))
            {
                await RestoreTcpIpPrinterAsync(printer, logger, cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(connectionPath))
            {
                logger.Add($"Printer '{printer.Name}' was saved for reference but could not be recreated automatically.");
                continue;
            }
        }
    }

    private static async Task RestoreTcpIpPrinterAsync(
        PrinterOption printer,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        var hostAddress = printer.PortName;
        var portName = printer.PortName;
        var printerName = printer.Name;
        var driverPackage = GetNormalizedPrinterDriverPackage(printer);
        var driverName = driverPackage?.DriverName ?? printer.DriverName;

        if (driverPackage is not null)
        {
            logger.Add($"Printer '{printerName}' is using standardized driver '{driverPackage.DriverName}'.");
            var driverInstalled = await EnsurePrinterDriverInstalledAsync(driverPackage, logger, cancellationToken);

            if (!driverInstalled)
            {
                logger.Add($"Printer '{printerName}' was not recreated because driver '{driverName}' could not be installed.");
                return;
            }
        }

        await RunProcessAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"try {{ if (-not (Get-PrinterPort -Name '{EscapePowerShell(portName)}' -ErrorAction SilentlyContinue)) {{ Add-PrinterPort -Name '{EscapePowerShell(portName)}' -PrinterHostAddress '{EscapePowerShell(hostAddress)}' -ErrorAction Stop }}; exit 0 }} catch {{ Write-Error $_; exit 1 }}\"",
            logger,
            cancellationToken);

        await RunProcessAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"try {{ if (-not (Get-Printer -Name '{EscapePowerShell(printerName)}' -ErrorAction SilentlyContinue)) {{ Add-Printer -Name '{EscapePowerShell(printerName)}' -DriverName '{EscapePowerShell(driverName)}' -PortName '{EscapePowerShell(portName)}' -ErrorAction Stop }}; exit 0 }} catch {{ Write-Error $_; exit 1 }}\"",
            logger,
            cancellationToken);

        if (printer.IsDefault)
        {
            logger.Add($"Printer '{printerName}' was the source default. The restored user must set the default after first sign-in because Windows stores that choice per user.");
        }
    }

    private static StandardPrinterDriverPackage? GetNormalizedPrinterDriverPackage(PrinterOption printer)
    {
        var combinedText = $"{printer.Name} {printer.DriverName}";

        if (combinedText.Contains("Canon", StringComparison.OrdinalIgnoreCase))
        {
            return new StandardPrinterDriverPackage(
                "Canon Generic Plus UFR II",
                @"C:\CIS\Printer Drivers\Canon Canon Printer Drivers 3.15\Driver\CNLB0MA64.INF");
        }

        if (combinedText.Contains("HP", StringComparison.OrdinalIgnoreCase))
        {
            return new StandardPrinterDriverPackage(
                "HP Universal Printing PCL 6",
                @"C:\CIS\Printer Drivers\HP Universal Printer Driver 7.4\hpcu315u.inf");
        }

        return null;
    }

    private static async Task<bool> EnsurePrinterDriverInstalledAsync(
        StandardPrinterDriverPackage driverPackage,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        var driverName = driverPackage.DriverName;

        if (await IsPrinterDriverInstalledAsync(driverName, logger, cancellationToken))
        {
            logger.Add($"Printer driver '{driverName}' is already installed.");
            return true;
        }

        if (!File.Exists(driverPackage.InfPath))
        {
            logger.Add($"Printer driver INF was not found: {driverPackage.InfPath}");
            return false;
        }

        logger.Add($"Installing printer driver '{driverName}' from '{driverPackage.InfPath}'.");

        await RunProcessAsync(
            "pnputil.exe",
            $"/add-driver \"{driverPackage.InfPath}\" /install",
            logger,
            cancellationToken);

        await RunProcessAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"try {{ Add-PrinterDriver -Name '{EscapePowerShell(driverName)}' -ErrorAction Stop; exit 0 }} catch {{ Write-Error $_; exit 1 }}\"",
            logger,
            cancellationToken);

        if (await IsPrinterDriverInstalledAsync(driverName, logger, cancellationToken))
        {
            logger.Add($"Printer driver '{driverName}' is now installed.");
            return true;
        }

        await RunProcessAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"try {{ Add-PrinterDriver -Name '{EscapePowerShell(driverName)}' -InfPath '{EscapePowerShell(driverPackage.InfPath)}' -ErrorAction Stop; exit 0 }} catch {{ Write-Error $_; exit 1 }}\"",
            logger,
            cancellationToken);

        if (await IsPrinterDriverInstalledAsync(driverName, logger, cancellationToken))
        {
            logger.Add($"Printer driver '{driverName}' is now installed.");
            return true;
        }

        await RunProcessAsync(
            "rundll32.exe",
            $"printui.dll,PrintUIEntry /ia /m \"{driverName}\" /f \"{driverPackage.InfPath}\" /h \"x64\" /v \"Type 3 - User Mode\"",
            logger,
            cancellationToken);

        if (await IsPrinterDriverInstalledAsync(driverName, logger, cancellationToken))
        {
            logger.Add($"Printer driver '{driverName}' is now installed.");
            return true;
        }

        logger.Add($"Printer driver '{driverName}' still does not appear to be installed after the install attempt.");
        return false;
    }

    private static async Task<bool> IsPrinterDriverInstalledAsync(
        string driverName,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-PrinterDriver -Name '{EscapePowerShell(driverName)}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            var filteredStdout = FilterProcessOutput(startInfo.FileName, stdout).Trim();
            if (!string.IsNullOrWhiteSpace(filteredStdout))
            {
                logger.Add(filteredStdout);
            }
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            var filteredStderr = FilterProcessOutput(startInfo.FileName, stderr).Trim();
            if (!string.IsNullOrWhiteSpace(filteredStderr))
            {
                logger.Add(filteredStderr);
            }
        }

        return process.ExitCode == 0;
    }

    private static async Task RunProcessAsync(
        string fileName,
        string arguments,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            logger.Add(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            logger.Add(stderr.Trim());
        }

        logger.Add($"{fileName} exited with code {process.ExitCode}.");
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FilterProcessOutput(string fileName, string output)
    {
        if (!fileName.Equals("icacls.exe", StringComparison.OrdinalIgnoreCase))
        {
            return output;
        }

        var filteredLines = output
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !ShouldSuppressIcaclsLine(line))
            .ToArray();

        return string.Join(Environment.NewLine, filteredLines).Trim();
    }

    private static bool ShouldSuppressIcaclsLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        if (!line.Contains("Access is denied.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return line.Contains(@"\AppData\Local\Application Data\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\AppData\Local\History\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\AppData\Local\Microsoft\Windows\INetCache\Content.IE5\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\AppData\Local\Microsoft\Windows\Temporary Internet Files\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\AppData\Local\Temporary Internet Files\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Application Data\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Cookies\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Documents\My Music\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Documents\My Pictures\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Documents\My Videos\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Local Settings\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\My Documents\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\NetHood\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\PrintHood\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Recent\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\SendTo\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Start Menu\", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(@"\Templates\", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRestoreLogFileName(string? userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return $"{safeUserName}-restore-log.txt";
    }

    private static bool IsAcceptableSevenZipExitCode(int exitCode) => exitCode is 0 or 1;

    private static bool ShouldRestoreProfilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        return !PortableProfilePolicy.ShouldExcludeFile(Path.GetFileName(relativePath)) &&
               !PortableProfilePolicy.IsMachineSpecificPath(relativePath);
    }

    private static string GetRestoreApproachLogText(AllocatorSession session) =>
        session.RestoreCollisionMode == RestoreCollisionMode.OverwriteExistingProfile
            ? "Ask Windows to create a fresh profile, then restore portable user data."
            : "Restore files into an existing working profile.";

    private static string GetProfileHivePlanLogText(AllocatorSession session) =>
        "Keep the destination Windows-created NTUSER.DAT and UsrClass.dat; never restore source profile hives.";

    private static string GetShellStatePlanLogText(AllocatorSession session) =>
        "Keep destination Windows shell and AppData\\Local state; restore portable data only.";

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

    private static string GetProfilesRootPath()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return Path.Combine(string.IsNullOrWhiteSpace(systemDrive) ? @"C:" : systemDrive, "Users");
    }

    private static void SafeDeleteDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RestoreLogger
    {
        private readonly object _gate = new();
        private readonly string _path;
        private readonly List<string> _messages = [];
        private readonly TelemetryService _telemetry;

        public RestoreLogger(string path, TelemetryService telemetry)
        {
            _path = path;
            _telemetry = telemetry;
        }

        public List<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
                }
            }
        }

        public void Add(string message)
        {
            lock (_gate)
            {
                _messages.Add(message);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? Path.GetTempPath());
                    File.WriteAllLines(_path, _messages);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                WriteTelemetry(message);
            }
        }

        private void WriteTelemetry(string message)
        {
            var level = InferLevel(message);
            if (level == "error")
            {
                _telemetry.WriteError(message, phase: "restore", status: "failed");
                return;
            }

            if (level == "warning")
            {
                _telemetry.WriteWarning(message, phase: "restore", status: "warning");
                return;
            }

            _telemetry.WriteInfo(message, phase: "restore", status: "running");
        }

        private static string InferLevel(string message)
        {
            if (message.Contains("Failed processing 0 files", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Successfully processed", StringComparison.OrdinalIgnoreCase))
            {
                return "info";
            }

            if (message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                return "error";
            }

            if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("skipped", StringComparison.OrdinalIgnoreCase))
            {
                return "warning";
            }

            return "info";
        }
    }

    private sealed record StandardPrinterDriverPackage(string DriverName, string InfPath);
}
