using System.Printing;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class PrinterDiscoveryService
{
    private const string StandardTcpIpPortsRegistryPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports";

    public IReadOnlyList<PrinterOption> GetPrinters()
    {
        try
        {
            var server = new LocalPrintServer();
            var defaultPrinterName = server.DefaultPrintQueue?.Name ?? string.Empty;

            return server
                .GetPrintQueues([
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                ])
                .OrderBy(queue => queue.Name)
                .Select(queue => new PrinterOption
                {
                    Name = queue.Name,
                    IsDefault = string.Equals(queue.Name, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
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
            var server = new LocalPrintServer();
            var defaultPrinterName = server.DefaultPrintQueue?.Name ?? string.Empty;

            return server
                .GetPrintQueues([
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                ])
                .Where(queue => selectedNames.Contains(queue.Name))
                .OrderBy(queue => queue.Name)
                .Select(queue =>
                {
                    var portName = queue.QueuePort?.Name ?? string.Empty;
                    return new BackupPrinterInfo
                    {
                        Name = queue.Name,
                        IsDefault = string.Equals(queue.Name, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
                        DriverName = queue.QueueDriver?.Name ?? string.Empty,
                        PortName = portName,
                        HostAddress = GetTcpIpHostAddress(portName),
                        IsNetworkPrinter = portName.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) || queue.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase),
                        ConnectionPath = queue.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ? queue.Name : string.Empty
                    };
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
}
