namespace TheAllocator.Models;

public sealed class TelemetryEvent
{
    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = "info";

    public string App { get; set; } = "The Allocator";

    public string JobId { get; set; } = string.Empty;

    public string Computer { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? Tech { get; set; }

    public string? UserProfile { get; set; }

    public string? SourceComputer { get; set; }

    public string? TargetComputer { get; set; }

    public string? Phase { get; set; }

    public string? Status { get; set; }

    public string? ErrorCode { get; set; }

    public string? ExceptionType { get; set; }

    public string? Path { get; set; }

    public double? DurationSeconds { get; set; }

    public int? FilesCopied { get; set; }

    public int? FilesSkipped { get; set; }

    public long? BytesCopied { get; set; }

    public int? WarningCount { get; set; }

    public int? ErrorCount { get; set; }

    public string? BackupPath { get; set; }

    public string? RestorePath { get; set; }

    public string? SourceOperatingSystem { get; set; }

    public string? TargetOperatingSystem { get; set; }
}
