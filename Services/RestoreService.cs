using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class RestoreService
{
    public RestoreService(SevenZipService sevenZipService)
    {
        SevenZipService = sevenZipService;
    }

    public SevenZipService SevenZipService { get; }

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
        var logger = new RestoreLogger(restoreLogPath);

        try
        {
            logger.Add($"Restore started at {DateTime.Now:u}");
            logger.Add($"Restore package: {session.RestorePackagePath}");
            logger.Add($"Target profile path: {targetProfilePath}");
            logger.Add($"Collision mode: {session.RestoreCollisionMode}");
            logger.Add($"Source operating system: {session.RestoreManifest.SourceOperatingSystem} ({session.RestoreManifest.SourceOperatingSystemVersion})");
            logger.Add($"Target operating system: {MachineInfoService.GetOperatingSystemDisplayName()} ({MachineInfoService.GetOperatingSystemVersionValue()})");
            ValidateCrossUserOverwrite(session, logger);
            ValidateTargetProfileIsNotCurrentSignedInProfile(targetProfilePath, logger);

            IProgress<string> progressProxy = new Progress<string>(message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    logger.Add(message);
                    progress?.Report(message);
                }
            });

            progressProxy.Report("Preparing target profile folder...");
            PrepareTargetProfilePath(targetProfilePath, session.RestoreCollisionMode, logger);

            progressProxy.Report("Applying profile permissions...");
            await ApplyBasePermissionsAsync(targetProfilePath, session, logger, cancellationToken);

            progressProxy.Report("Extracting files directly into the target profile...");
            var copiedFileCount = await ExtractProfileContentDirectlyAsync(
                session.RestorePackagePath,
                session.RestoreManifest,
                session,
                targetProfilePath,
                progressProxy,
                logger,
                cancellationToken);

            progressProxy.Report("Finalizing profile access...");
            await ApplyProfileHivePermissionsAsync(targetProfilePath, session, logger, cancellationToken);

            progressProxy.Report("Reconnecting the profile to the target account...");
            TryBindProfileToAccount(targetProfilePath, session, logger);

            progressProxy.Report("Restoring selected printers...");
            await RestorePrintersAsync(session, logger, cancellationToken);

            logger.Add($"Copied files: {copiedFileCount:N0}");
            logger.Add($"Restore finished at {DateTime.Now:u}");

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

        if (string.Equals(sourceUser, targetUser, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        logger.Add($"Cross-user overwrite restore was blocked. Backup user '{session.RestoreManifest?.UserName}' does not match target user '{session.RestoreTargetUser}'.");
        throw new InvalidOperationException(
            "Overwrite restore is only allowed when the backup belongs to the same user as the target account. For a different user, use merge instead.");
    }

    private static void PrepareTargetProfilePath(string targetProfilePath, RestoreCollisionMode collisionMode, RestoreLogger logger)
    {
        if (!Directory.Exists(targetProfilePath))
        {
            if (collisionMode == RestoreCollisionMode.MergeIntoExistingProfile)
            {
                logger.Add($"Merge restore was blocked because the target profile folder does not exist: {targetProfilePath}");
                throw new InvalidOperationException(
                    "Merge restore requires an existing healthy Windows profile. Sign into the target account once first, or use overwrite restore for a same-user profile rebuild.");
            }

            Directory.CreateDirectory(targetProfilePath);
            logger.Add($"Created target profile folder: {targetProfilePath}");
            return;
        }

        if (collisionMode == RestoreCollisionMode.OverwriteExistingProfile)
        {
            SafeDeleteDirectory(targetProfilePath);
            Directory.CreateDirectory(targetProfilePath);
            logger.Add($"Existing profile folder was removed and recreated: {targetProfilePath}");
            return;
        }

        logger.Add($"Existing profile folder will be merged: {targetProfilePath}");
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
            .Where(path => ShouldRestoreProfilePath(path, session))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skippedHivePaths = manifest.IncludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !ShouldRestoreProfilePath(path, session))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var skippedPath in skippedHivePaths)
        {
            logger.Add($"Skipped sensitive profile state during restore: {skippedPath}");
        }

        if (IsSameUserOverwriteRestore(session) && !IsLegacySourceRestore(session))
        {
            logger.Add("Same-user overwrite restore detected. Profile registry hives will be restored.");
        }

        if (IsLegacySourceRestore(session))
        {
            logger.Add("Legacy source operating system detected. Restore will skip profile hives and Windows shell state for compatibility.");
        }

        var extractCode = await SevenZipService.ExtractArchiveAsync(
            archivePath,
            targetProfilePath,
            progress,
            cancellationToken,
            GetLegacyRestoreExcludePatterns(session),
            includePatterns);

        if (!IsAcceptableSevenZipExitCode(extractCode))
        {
            throw new InvalidOperationException($"The backup archive could not be extracted. 7-Zip returned exit code {extractCode}.");
        }

        if (extractCode == 1)
        {
            logger.Add("7-Zip reported warnings during direct restore extraction, but the restore continued.");
        }

        return CountRestoredFiles(targetProfilePath, includePatterns, logger);
    }

    private static int CountRestoredFiles(string targetProfilePath, IEnumerable<string> includedPaths, RestoreLogger logger)
    {
        var restoredFiles = 0;

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
                    restoredFiles += Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Count();
                }
            }
            catch (Exception ex)
            {
                logger.Add($"Could not count restored content for {relativePath}: {ex.Message}");
            }
        }

        return restoredFiles;
    }

    private static async Task ApplyBasePermissionsAsync(
        string targetProfilePath,
        AllocatorSession session,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        var icaclsIdentity = GetIcaclsIdentity(session);

        await RunProcessAsync(
            "icacls.exe",
            $"\"{targetProfilePath}\" /inheritance:e",
            logger,
            cancellationToken);

        await RunProcessAsync(
            "icacls.exe",
            $"\"{targetProfilePath}\" /grant:r \"Administrators:(OI)(CI)F\"",
            logger,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(icaclsIdentity))
        {
            await RunProcessAsync(
                "icacls.exe",
                $"\"{targetProfilePath}\" /grant:r \"{icaclsIdentity}:(OI)(CI)F\"",
                logger,
                cancellationToken);
        }

        if (session.RestoreCollisionMode == RestoreCollisionMode.MergeIntoExistingProfile &&
            Directory.Exists(targetProfilePath))
        {
            logger.Add("Existing profile merge selected. Expanding permissions recursively on the target profile to avoid access denied errors during extraction.");

            await RunProcessAsync(
                "icacls.exe",
                $"\"{targetProfilePath}\" /grant:r \"Administrators:(OI)(CI)F\" /T /C",
                logger,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(icaclsIdentity))
            {
                await RunProcessAsync(
                    "icacls.exe",
                    $"\"{targetProfilePath}\" /grant:r \"{icaclsIdentity}:(OI)(CI)F\" /T /C",
                    logger,
                    cancellationToken);
            }
        }

        logger.Add("Permissions were applied to the target profile folder so restored files inherit access as they are extracted.");
    }

    private static async Task ApplyProfileHivePermissionsAsync(
        string targetProfilePath,
        AllocatorSession session,
        RestoreLogger logger,
        CancellationToken cancellationToken)
    {
        if (!IsSameUserOverwriteRestore(session))
        {
            return;
        }

        var icaclsIdentity = GetIcaclsIdentity(session);
        if (string.IsNullOrWhiteSpace(icaclsIdentity))
        {
            return;
        }

        var hivePaths = new[]
        {
            Path.Combine(targetProfilePath, "NTUSER.DAT"),
            Path.Combine(targetProfilePath, "AppData", "Local", "Microsoft", "Windows", "UsrClass.dat")
        };

        foreach (var hivePath in hivePaths.Where(File.Exists))
        {
            logger.Add($"Applying explicit access to restored profile hive: {hivePath}");

            await RunProcessAsync(
                "icacls.exe",
                $"\"{hivePath}\" /grant:r \"Administrators:F\" \"SYSTEM:F\" \"{icaclsIdentity}:F\" /C",
                logger,
                cancellationToken);
        }
    }

    private static string GetIcaclsIdentity(AllocatorSession session)
    {
        if (string.IsNullOrWhiteSpace(session.RestoreTargetUser))
        {
            return string.Empty;
        }

        if (session.RestoreUseDomainAccount)
        {
            return session.RestoreTargetAccountDisplay;
        }

        return $@"{Environment.MachineName}\{session.RestoreTargetUser}";
    }

    private static void TryBindProfileToAccount(string targetProfilePath, AllocatorSession session, RestoreLogger logger)
    {
        try
        {
            var accountName = session.RestoreUseDomainAccount
                ? session.RestoreTargetAccountDisplay
                : $@"{Environment.MachineName}\{session.RestoreTargetUser}";

            var sid = (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            using var profileListKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList", writable: true);

            if (profileListKey?.OpenSubKey($"{sid.Value}.bak") is not null)
            {
                profileListKey.DeleteSubKeyTree($"{sid.Value}.bak", throwOnMissingSubKey: false);
                logger.Add($"Removed stale profile registry backup key for {accountName}.");
            }

            using var userKey = profileListKey?.CreateSubKey(sid.Value);
            if (userKey is null)
            {
                logger.Add($"Could not open or create the profile registry key for {accountName}.");
                return;
            }

            userKey.SetValue("ProfileImagePath", targetProfilePath, RegistryValueKind.ExpandString);
            userKey.SetValue("Flags", 0, RegistryValueKind.DWord);
            userKey.SetValue("State", 0, RegistryValueKind.DWord);
            userKey.SetValue("RefCount", 0, RegistryValueKind.DWord);
            logger.Add($"Updated profile registry mapping for {accountName}.");
        }
        catch (Exception ex)
        {
            logger.Add($"Profile registry mapping was skipped: {ex.Message}");
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
                    $"printui.dll,PrintUIEntry /in /n \"{connectionPath}\"",
                    logger,
                    cancellationToken);

                if (printer.IsDefault)
                {
                    await RunProcessAsync(
                        "powershell.exe",
                        $"-NoProfile -ExecutionPolicy Bypass -Command \"Set-Printer -Name '{EscapePowerShell(printer.Name)}' -IsDefault $true\"",
                        logger,
                        cancellationToken);
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
            await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Set-Printer -Name '{EscapePowerShell(printerName)}' -IsDefault $true\"",
                logger,
                cancellationToken);
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
            logger.Add(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            logger.Add(stderr.Trim());
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

    private static string GetRestoreLogFileName(string? userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "selecteduser" : userName.Trim();
        return $"{safeUserName}-restore-log.txt";
    }

    private static bool IsAcceptableSevenZipExitCode(int exitCode) => exitCode is 0 or 1;

    private static bool ShouldRestoreProfilePath(string relativePath, AllocatorSession session)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (IsProfileHiveLogPath(relativePath))
        {
            return false;
        }

        if (ShouldSkipLegacyShellState(relativePath, session))
        {
            return false;
        }

        if (IsProfileHivePath(relativePath))
        {
            return IsSameUserOverwriteRestore(session) && !IsLegacySourceRestore(session);
        }

        return true;
    }

    private static bool IsProfileHivePath(string relativePath) =>
        relativePath.Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Equals(Path.Combine("AppData", "Local", "Microsoft", "Windows", "UsrClass.dat"), StringComparison.OrdinalIgnoreCase);

    private static bool IsProfileHiveLogPath(string relativePath) =>
        relativePath.StartsWith("NTUSER.DAT.LOG", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("UsrClass.dat.LOG", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameUserOverwriteRestore(AllocatorSession session)
    {
        var sourceUser = GetComparableAccountName(session.RestoreManifest?.UserName);
        var targetUser = GetComparableAccountName(session.RestoreTargetUser);

        return session.RestoreCollisionMode == RestoreCollisionMode.OverwriteExistingProfile &&
               !string.IsNullOrWhiteSpace(sourceUser) &&
               !string.IsNullOrWhiteSpace(targetUser) &&
               string.Equals(sourceUser, targetUser, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacySourceRestore(AllocatorSession session)
    {
        var versionValue = session.RestoreManifest?.SourceOperatingSystemVersion;
        if (Version.TryParse(versionValue, out var parsedVersion))
        {
            return parsedVersion.Major < 10;
        }

        var sourceOperatingSystem = session.RestoreManifest?.SourceOperatingSystem ?? string.Empty;
        return sourceOperatingSystem.Contains("Windows 7", StringComparison.OrdinalIgnoreCase) ||
               sourceOperatingSystem.Contains("Windows 8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipLegacyShellState(string relativePath, AllocatorSession session)
    {
        if (!IsLegacySourceRestore(session))
        {
            return false;
        }

        return relativePath.StartsWith(Path.Combine("AppData", "Local", "Microsoft", "Windows"), StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetLegacyRestoreExcludePatterns(AllocatorSession session)
    {
        if (!IsLegacySourceRestore(session))
        {
            return [];
        }

        return
        [
            Path.Combine("AppData", "Local", "Microsoft", "Windows")
        ];
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

        public RestoreLogger(string path)
        {
            _path = path;
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
        }
    }

    private sealed record StandardPrinterDriverPackage(string DriverName, string InfPath);
}
