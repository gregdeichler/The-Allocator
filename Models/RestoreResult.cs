namespace TheAllocator.Models;

public sealed class RestoreResult
{
    public bool Success { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;

    public string RestoreLogPath { get; init; } = string.Empty;

    public string TargetProfilePath { get; init; } = string.Empty;

    public int CopiedFileCount { get; init; }

    public List<string> Messages { get; init; } = [];
}
