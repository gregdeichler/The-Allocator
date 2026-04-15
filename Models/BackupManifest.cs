namespace TheAllocator.Models;

public sealed class BackupManifest
{
    public string AppName { get; set; } = "The Allocator";

    public string AppVersion { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string SourceComputerName { get; set; } = string.Empty;

    public string SourceOperatingSystem { get; set; } = string.Empty;

    public string SourceOperatingSystemVersion { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Sid { get; set; } = string.Empty;

    public string ProfilePath { get; set; } = string.Empty;

    public bool IsDomainLinked { get; set; }

    public string ArchiveFileName { get; set; } = string.Empty;

    public long ArchiveSizeBytes { get; set; }

    public string MetadataFileName { get; set; } = string.Empty;

    public string PrintersFileName { get; set; } = string.Empty;

    public string LogFileName { get; set; } = "backup-log.txt";

    public List<string> IncludedPaths { get; set; } = [];

    public List<string> ExcludedPaths { get; set; } = [];
}
