using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TheAllocator.WinUI.Infrastructure;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync();
    Task<string?> PickBackupPackageAsync();
}

public sealed class FilePickerService(nint windowHandle) : IFilePickerService
{
    public async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, windowHandle);
            return (await picker.PickSingleFolderAsync())?.Path;
        }
        catch (Exception ex) when (Win32FileDialog.IsPickerIntegrationFailure(ex))
        {
            return Win32FileDialog.PickFolder(windowHandle, "Choose the backup destination");
        }
    }

    public async Task<string?> PickBackupPackageAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".7z");
            InitializeWithWindow.Initialize(picker, windowHandle);
            return (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception ex) when (Win32FileDialog.IsPickerIntegrationFailure(ex))
        {
            return Win32FileDialog.PickFile(windowHandle, "Choose an Allocator backup", "Allocator backups (*.7z)", "*.7z");
        }
    }
}
