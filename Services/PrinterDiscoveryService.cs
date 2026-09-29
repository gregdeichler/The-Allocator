using System.Management;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class PrinterDiscoveryService : IPrinterDiscoveryService
{
    private const string StandardTcpIpPortsRegistryPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports";

    public IReadOnlyList<PrinterOption> GetPrinters()
    {
        try
        {
            return ReadInstalledPrinters()
                .Select(printer => new PrinterOption
                {
                    Name = printer.Name,
                    IsDefault = printer.IsDefault,
                    DriverName = printer.DriverName,
                    PortName = printer.PortName,
                    HostAddress = GetTcpIpHostAddress(printer.PortName),
                    IsNetworkPrinter = printer.IsNetworkPrinter,
                    ConnectionPath = printer.ConnectionPath,
                    IsSelected = true
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<BackupPrinterInfo> GetPrinterDetails(IEnumerable<PrinterOption> selectedPrinters)
    {
        var selectedNames = selectedPrinters
            .Where(printer => printer.IsSelected)
            .Select(printer => printer.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedNames.Count == 0)
        {
            return [];
        }

        try
        {
            return ReadInstalledPrinters()
                .Where(printer => selectedNames.Contains(printer.Name))
                .Select(printer => new BackupPrinterInfo
                {
                    Name = printer.Name,
                    IsDefault = printer.IsDefault,
                    DriverName = printer.DriverName,
                    PortName = printer.PortName,
                    HostAddress = GetTcpIpHostAddress(printer.PortName),
                    IsNetworkPrinter = printer.IsNetworkPrinter,
                    ConnectionPath = printer.ConnectionPath
                })
                .ToList();
        }
        catch
        {
            return selectedPrinters
                .Where(printer => printer.IsSelected)
                .Select(printer => new BackupPrinterInfo
                {
                    Name = printer.Name,
                    IsDefault = printer.IsDefault,
                    DriverName = printer.DriverName,
                    PortName = printer.PortName,
                    HostAddress = printer.HostAddress,
                    IsNetworkPrinter = printer.IsNetworkPrinter || printer.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase),
                    ConnectionPath = !string.IsNullOrWhiteSpace(printer.ConnectionPath)
                        ? printer.ConnectionPath
                        : printer.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
                            ? printer.Name
                            : string.Empty
                })
                .ToList();
        }
    }

    private static IReadOnlyList<InstalledPrinter> ReadInstalledPrinters()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DriverName, PortName, Default, Network FROM Win32_Printer");
        using var results = searcher.Get();
        var printers = new List<InstalledPrinter>();

        foreach (ManagementObject result in results)
        {
            using (result)
            {
                var name = result["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var portName = result["PortName"]?.ToString()?.Trim() ?? string.Empty;
                var isNetwork = result["Network"] is bool network && network ||
                    name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
                    portName.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
                printers.Add(new InstalledPrinter(
                    name,
                    result["DriverName"]?.ToString()?.Trim() ?? string.Empty,
                    portName,
                    result["Default"] is bool isDefault && isDefault,
                    isNetwork,
                    name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ? name : string.Empty));
            }
        }

        return printers.OrderBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetTcpIpHostAddress(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName) || portName.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        try
        {
            using var portsKey = Registry.LocalMachine.OpenSubKey(StandardTcpIpPortsRegistryPath);
            using var portKey = portsKey?.OpenSubKey(portName);
            var hostName = portKey?.GetValue("HostName") as string;
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                return hostName.Trim();
            }

            var ipAddress = portKey?.GetValue("IPAddress") as string;
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                return ipAddress.Trim();
            }
        }
        catch
        {
            // Registry access is best-effort. Safe legacy inference below is used only
            // when the port name itself clearly represents an address.
        }

        return PrinterPortAddressPolicy.TryInferLegacyHostAddress(portName, out var inferred)
            ? inferred
            : string.Empty;
    }

    private sealed record InstalledPrinter(
        string Name,
        string DriverName,
        string PortName,
        bool IsDefault,
        bool IsNetworkPrinter,
        string ConnectionPath);
}
