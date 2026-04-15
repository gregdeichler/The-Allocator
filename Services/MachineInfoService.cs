using System.IO;
using Microsoft.Win32;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class MachineInfoService
{
    public static string GetOperatingSystemDisplayName()
    {
        try
        {
            using var currentVersionKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (currentVersionKey is not null)
            {
                var productName = currentVersionKey.GetValue("ProductName")?.ToString()?.Trim();
                var displayVersion = currentVersionKey.GetValue("DisplayVersion")?.ToString()?.Trim();
                var releaseId = currentVersionKey.GetValue("ReleaseId")?.ToString()?.Trim();

                var versionLabel = !string.IsNullOrWhiteSpace(displayVersion)
                    ? displayVersion
                    : releaseId;

                if (!string.IsNullOrWhiteSpace(productName))
                {
                    return string.IsNullOrWhiteSpace(versionLabel)
                        ? productName
                        : $"{productName} {versionLabel}";
                }
            }
        }
        catch
        {
        }

        return Environment.OSVersion.VersionString;
    }

    public static string GetOperatingSystemVersionValue() => Environment.OSVersion.Version.ToString();

    public MachineInfoSnapshot GetSnapshot()
    {
        var deviceName = Environment.MachineName;
        var model = "Unknown model";
        var totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (biosKey is not null)
            {
                var manufacturer = biosKey.GetValue("SystemManufacturer")?.ToString()?.Trim();
                var machineModel = biosKey.GetValue("SystemProductName")?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(machineModel))
                {
                    model = string.IsNullOrWhiteSpace(manufacturer)
                        ? machineModel
                        : $"{manufacturer} {machineModel}";
                }
            }
        }
        catch
        {
        }

        var totalFixedStorageBytes = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Sum(drive => drive.TotalSize);

        return new MachineInfoSnapshot
        {
            DeviceName = deviceName,
            Model = model,
            MemorySummary = totalMemoryBytes > 0 ? SizeFormattingService.ToReadableSize(totalMemoryBytes) + " RAM" : "RAM unavailable",
            StorageSummary = totalFixedStorageBytes > 0
                ? SizeFormattingService.ToReadableSize(totalFixedStorageBytes) + " internal storage"
                : "Storage unavailable"
        };
    }
}
