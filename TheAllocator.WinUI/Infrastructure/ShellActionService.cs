using System.Diagnostics;

namespace TheAllocator.WinUI.Infrastructure;

public interface IShellActionService
{
    void OpenFolder(string path);
    void OpenLog(string path);
    void RebootComputer();
}

public sealed class ShellActionService : IShellActionService
{
    public void OpenFolder(string path)
    {
        var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        }
    }

    public void OpenLog(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public void RebootComputer() =>
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
}
