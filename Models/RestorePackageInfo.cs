namespace TheAllocator.Models;

public sealed class RestorePackageInfo
{
    public bool Success { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;

    public BackupManifest? Manifest { get; init; }

    public List<BackupPrinterInfo> Printers { get; init; } = [];

    public string MetadataPath { get; init; } = string.Empty;

    public string PrintersPath { get; init; } = string.Empty;
}
