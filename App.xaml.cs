using System;
using System.Runtime.InteropServices;
using System.Windows;
using TheAllocator.Services;

namespace TheAllocator;

public partial class App : System.Windows.Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetThreadExecutionState(
            ExecutionState.Continuous |
            ExecutionState.SystemRequired |
            ExecutionState.DisplayRequired);
        _ = TelemetryService.FlushWellKnownPendingBatchesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SetThreadExecutionState(ExecutionState.Continuous);
        try
        {
            TelemetryService.FlushWellKnownPendingBatchesAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        base.OnExit(e);
    }

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000
    }
}
