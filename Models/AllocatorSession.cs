namespace TheAllocator.Models;

public sealed class AllocatorSession
{
    public ProfileOption? SelectedBackupProfile { get; set; }

    public List<PrinterOption> AvailableBackupPrinters { get; set; } = [];

    public List<PrinterOption> SelectedBackupPrinters { get; set; } = [];

    public string BackupDestinationFolder { get; set; } = string.Empty;

    public string BackupPackageName { get; set; } = string.Empty;

    public string BackupPackagePath { get; set; } = string.Empty;

    public string BackupMetadataFileName { get; set; } = string.Empty;

    public string BackupPrintersFileName { get; set; } = string.Empty;

    public string BackupLogFileName { get; set; } = string.Empty;

    public DateTime? BackupStartedAt { get; set; }

    public DateTime? BackupCompletedAt { get; set; }

    public long BackupArchiveSizeBytes { get; set; }

    public string BackupErrorMessage { get; set; } = string.Empty;

    public string RestorePackagePath { get; set; } = string.Empty;

    public BackupManifest? RestoreManifest { get; set; }

    public string RestoreMetadataPath { get; set; } = string.Empty;

    public string RestorePrintersPath { get; set; } = string.Empty;

    public string RestoreLogPath { get; set; } = string.Empty;

    public List<PrinterOption> AvailableRestorePrinters { get; set; } = [];

    public List<PrinterOption> SelectedRestorePrinters { get; set; } = [];

    public ProfileOption? SelectedRestoreExistingProfile { get; set; }

    public string RestoreTargetUser { get; set; } = string.Empty;

    public bool RestoreUseExistingAccount { get; set; }

    public bool RestoreUseDomainAccount { get; set; } = true;

    public string RestoreTargetAccountDisplay { get; set; } = string.Empty;

    public DateTime? RestoreStartedAt { get; set; }

    public DateTime? RestoreCompletedAt { get; set; }

    public string RestoreTargetProfilePath { get; set; } = string.Empty;

    public string RestoreErrorMessage { get; set; } = string.Empty;

    public int RestoreCopiedFileCount { get; set; }

    public RestoreCollisionMode RestoreCollisionMode { get; set; } = RestoreCollisionMode.MergeIntoExistingProfile;
}
