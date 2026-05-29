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

    public string BackupDisplayDetail =>
        string.IsNullOrWhiteSpace(PortName)
            ? DriverName
            : $"{DriverName} • {PortName}";

    public string RestoreDisplayDetail
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(ConnectionPath))
            {
                parts.Add($"Connection: {ConnectionPath}");
            }

            if (!string.IsNullOrWhiteSpace(PortName))
            {
                parts.Add($"Port: {PortName}");
            }

            if (!string.IsNullOrWhiteSpace(DriverName))
            {
                parts.Add($"Driver: {DriverName}");
            }

            return string.Join(" • ", parts);
        }
    }
}
