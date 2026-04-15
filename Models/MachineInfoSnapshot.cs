namespace TheAllocator.Models;

public sealed class MachineInfoSnapshot
{
    public string DeviceName { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string MemorySummary { get; init; } = string.Empty;

    public string StorageSummary { get; init; } = string.Empty;
}
