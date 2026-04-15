namespace TheAllocator.Models;

public sealed class PrinterOption
{
    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    public string DriverName { get; init; } = string.Empty;

    public string PortName { get; init; } = string.Empty;

    public bool IsNetworkPrinter { get; init; }

    public string ConnectionPath { get; init; } = string.Empty;

    public bool IsSelected { get; set; } = true;
}
