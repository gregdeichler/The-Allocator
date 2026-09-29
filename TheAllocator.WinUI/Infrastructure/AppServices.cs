using TheAllocator.Services;

namespace TheAllocator.WinUI.Infrastructure;

public interface IAppServices
{
    IProfileDiscoveryService Profiles { get; }
    IPrinterDiscoveryService Printers { get; }
    IMachineInfoService MachineInfo { get; }
    IBackupService Backup { get; }
    IRestoreService Restore { get; }
    SevenZipService SevenZip { get; }
    WindowsProfileService WindowsProfiles { get; }
    IFilePickerService Pickers { get; }
    IShellActionService Shell { get; }
}

public sealed class AppServices : IAppServices
{
    public AppServices(nint windowHandle)
    {
        var sevenZip = new SevenZipService(AppContext.BaseDirectory);
        var profiles = new ProfileDiscoveryService();
        var printers = new PrinterDiscoveryService();
        var windowsProfiles = new WindowsProfileService();

        Profiles = profiles;
        Printers = printers;
        MachineInfo = new MachineInfoService();
        Backup = new BackupService(printers, sevenZip);
        Restore = new RestoreService(sevenZip, windowsProfiles);
        SevenZip = sevenZip;
        WindowsProfiles = windowsProfiles;
        Pickers = new FilePickerService(windowHandle);
        Shell = new ShellActionService();
    }

    public IProfileDiscoveryService Profiles { get; }
    public IPrinterDiscoveryService Printers { get; }
    public IMachineInfoService MachineInfo { get; }
    public IBackupService Backup { get; }
    public IRestoreService Restore { get; }
    public SevenZipService SevenZip { get; }
    public WindowsProfileService WindowsProfiles { get; }
    public IFilePickerService Pickers { get; }
    public IShellActionService Shell { get; }
}
