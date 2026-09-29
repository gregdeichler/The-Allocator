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
        UnhandledException += App_UnhandledException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);
            _ = TelemetryService.FlushWellKnownPendingBatchesAsync();
            _window = new MainWindow();
            _window.Closed += Window_Closed;
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupFailure(ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        WriteStartupFailure(e.Exception);

    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                $"[{DateTime.Now:u}]{Environment.NewLine}{exception}");
        }
        catch
        {
        }
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
