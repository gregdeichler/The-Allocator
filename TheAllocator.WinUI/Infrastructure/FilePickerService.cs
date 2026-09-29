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
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, windowHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public async Task<string?> PickBackupPackageAsync()
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
}
