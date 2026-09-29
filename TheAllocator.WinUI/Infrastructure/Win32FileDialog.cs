using System.Runtime.InteropServices;

namespace TheAllocator.WinUI.Infrastructure;

internal static class Win32FileDialog
{
    private const int CancelledHResult = unchecked((int)0x800704C7);

    public static bool IsPickerIntegrationFailure(Exception exception) =>
        exception.HResult != CancelledHResult;

    public static string? PickFolder(nint owner, string title) =>
        Show(owner, title, FileOpenOptions.PickFolders | FileOpenOptions.PathMustExist | FileOpenOptions.ForceFileSystem);

    public static string? PickFile(nint owner, string title, string filterName, string filterPattern) =>
        Show(
            owner,
            title,
            FileOpenOptions.FileMustExist | FileOpenOptions.PathMustExist | FileOpenOptions.ForceFileSystem,
            [new FilterSpec(filterName, filterPattern)]);

    private static string? Show(nint owner, string title, FileOpenOptions options, FilterSpec[]? filters = null)
    {
        IFileOpenDialog? dialog = null;
        IShellItem? result = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogCom();
            dialog.SetTitle(title);
            dialog.SetOptions(options | FileOpenOptions.NoChangeDirectory);
            if (filters is { Length: > 0 })
                dialog.SetFileTypes((uint)filters.Length, filters);

            var showResult = dialog.Show(owner);
            if (showResult == CancelledHResult) return null;
            Marshal.ThrowExceptionForHR(showResult);

            dialog.GetResult(out result);
            result.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pathPointer);
            try
            {
                return Marshal.PtrToStringUni(pathPointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
        finally
        {
            if (result is not null) Marshal.FinalReleaseComObject(result);
            if (dialog is not null) Marshal.FinalReleaseComObject(dialog);
        }
    }

    [Flags]
    private enum FileOpenOptions : uint
    {
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        NoChangeDirectory = 0x00000008,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct FilterSpec(string name, string pattern)
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Name = name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Pattern = pattern;
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogCom
    {
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint parent);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(FileOpenOptions options);
        void GetOptions(out FileOpenOptions options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int errorCode);
        void SetClientGuid(in Guid clientGuid);
        void ClearClientData();
        void SetFilter(nint filter);
        void GetResults(out nint items);
        void GetSelectedItems(out nint items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, in Guid handlerId, in Guid interfaceId, out nint interfacePointer);
        void GetParent(out IShellItem parent);
        void GetDisplayName(ShellItemDisplayName displayName, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }
}
