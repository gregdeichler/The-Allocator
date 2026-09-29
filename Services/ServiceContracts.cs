using TheAllocator.Models;

namespace TheAllocator.Services;

public interface IProfileDiscoveryService
{
    IReadOnlyList<ProfileOption> GetProfiles();
}

public interface IPrinterDiscoveryService
{
    IReadOnlyList<PrinterOption> GetPrinters();

    IReadOnlyList<BackupPrinterInfo> GetPrinterDetails(IEnumerable<PrinterOption> selectedPrinters);
}

public interface IMachineInfoService
{
    MachineInfoSnapshot GetSnapshot();

    string OperatingSystemDisplayName { get; }

    string OperatingSystemVersion { get; }
}

public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(
        AllocatorSession session,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IRestoreService
{
    Task<RestorePackageInfo> InspectPackageAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreAsync(
        AllocatorSession session,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public static class OperationProgressMapper
{
    public static OperationProgress FromBackupMessage(string message) =>
        FromMessage(WorkflowMode.Backup, message);

    public static OperationProgress FromRestoreMessage(string message) =>
        FromMessage(WorkflowMode.Restore, message);

    private static OperationProgress FromMessage(WorkflowMode mode, string message)
    {
        var normalized = message.Trim();
        var phase = normalized.Contains("validat", StringComparison.OrdinalIgnoreCase)
            ? OperationPhase.Validating
            : normalized.Contains("printer", StringComparison.OrdinalIgnoreCase)
                ? OperationPhase.Printers
                : normalized.Contains("extract", StringComparison.OrdinalIgnoreCase)
                    ? OperationPhase.Extracting
                    : normalized.Contains("archive", StringComparison.OrdinalIgnoreCase) || normalized.Contains("compress", StringComparison.OrdinalIgnoreCase)
                        ? OperationPhase.Archiving
                        : normalized.Contains("detail", StringComparison.OrdinalIgnoreCase) || normalized.Contains("metadata", StringComparison.OrdinalIgnoreCase)
                            ? OperationPhase.Metadata
                            : normalized.Contains("final", StringComparison.OrdinalIgnoreCase)
                                ? OperationPhase.Verifying
                                : OperationPhase.Preparing;

        var headline = phase switch
        {
            OperationPhase.Validating => "Checking migration readiness",
            OperationPhase.Printers => "Restoring selected printers",
            OperationPhase.Extracting => "Restoring profile files",
            OperationPhase.Archiving => "Creating the backup archive",
            OperationPhase.Metadata => "Writing backup details",
            OperationPhase.Verifying => "Finishing and verifying",
            _ => "Preparing the migration"
        };

        var fraction = TryGetPhaseFraction(normalized);
        return new OperationProgress(mode, phase, headline, normalized, fraction);
    }

    private static double? TryGetPhaseFraction(string message)
    {
        if (message.Contains("Phase 1 of 3", StringComparison.OrdinalIgnoreCase)) return 1d / 3d;
        if (message.Contains("Phase 2 of 3", StringComparison.OrdinalIgnoreCase)) return 2d / 3d;
        if (message.Contains("Phase 3 of 3", StringComparison.OrdinalIgnoreCase)) return 0.9d;
        return null;
    }
}
