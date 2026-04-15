namespace TheAllocator.Models;

public sealed class BackupResult
{
    public bool Success { get; init; }

    public string ArchivePath { get; init; } = string.Empty;

    public string MetadataPath { get; init; } = string.Empty;

    public string PrintersPath { get; init; } = string.Empty;

    public string LogPath { get; init; } = string.Empty;

    public long ArchiveSizeBytes { get; init; }

    public int CopiedFileCount { get; init; }

    public List<string> Messages { get; init; } = [];

    public string ErrorMessage { get; init; } = string.Empty;
}
