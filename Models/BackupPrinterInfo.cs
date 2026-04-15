namespace TheAllocator.Models;

public sealed class BackupPrinterInfo
{
    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    public string DriverName { get; init; } = string.Empty;

    public string PortName { get; init; } = string.Empty;

    public bool IsNetworkPrinter { get; init; }

    public string ConnectionPath { get; init; } = string.Empty;
}
