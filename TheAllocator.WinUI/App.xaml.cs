using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using TheAllocator.Services;

namespace TheAllocator.WinUI;

public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);
        _ = TelemetryService.FlushWellKnownPendingBatchesAsync();
        _window = new MainWindow();
        _window.Closed += Window_Closed;
        _window.Activate();
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        SetThreadExecutionState(ExecutionState.Continuous);
        try
        {
            TelemetryService.FlushWellKnownPendingBatchesAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000
    }
}
