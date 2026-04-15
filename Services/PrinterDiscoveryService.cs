using System.Printing;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class PrinterDiscoveryService
{
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
                .Select(queue => new BackupPrinterInfo
                {
                    Name = queue.Name,
                    IsDefault = string.Equals(queue.Name, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
                    DriverName = queue.QueueDriver?.Name ?? string.Empty,
                    PortName = queue.QueuePort?.Name ?? string.Empty,
                    IsNetworkPrinter = queue.QueuePort?.Name?.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) == true || queue.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase),
                    ConnectionPath = queue.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ? queue.Name : string.Empty
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
                    IsNetworkPrinter = printer.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase),
                    ConnectionPath = printer.Name.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ? printer.Name : string.Empty
                })
                .ToList();
        }
    }
}
