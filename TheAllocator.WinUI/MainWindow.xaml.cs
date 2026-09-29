using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheAllocator.Models;
using TheAllocator.WinUI.Infrastructure;
using TheAllocator.WinUI.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace TheAllocator.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private bool _enforcingMinimumSize;

    public MainWindow()
    {
        InitializeComponent();

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ViewModel = new MainViewModel(new AppServices(windowHandle));
        Bindings.Update();
        ViewModel.RebootRequested += RebootRequested;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
    }

    public MainViewModel ViewModel { get; }

    private void ConfigureWindow()
    {
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TheAllocator.ico"));
        _appWindow.Changed += AppWindow_Changed;
        _appWindow.Closing += AppWindow_Closing;

        var saved = WindowBoundsStore.Load();
        if (saved is not null)
        {
            _appWindow.MoveAndResize(new RectInt32(saved.Value.Position.X, saved.Value.Position.Y, saved.Value.Size.Width, saved.Value.Size.Height));
            return;
        }

        var size = new SizeInt32(1280, 820);
        _appWindow.Resize(size);
        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (display is not null)
        {
            var area = display.WorkArea;
            _appWindow.Move(new PointInt32(
                area.X + Math.Max(0, (area.Width - size.Width) / 2),
                area.Y + Math.Max(0, (area.Height - size.Height) / 2)));
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange && !_enforcingMinimumSize && (sender.Size.Width < 1024 || sender.Size.Height < 720))
        {
            _enforcingMinimumSize = true;
            sender.Resize(new SizeInt32(Math.Max(1024, sender.Size.Width), Math.Max(720, sender.Size.Height)));
            _enforcingMinimumSize = false;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (ViewModel.IsTransferActive)
        {
            args.Cancel = true;
            ViewModel.ReportCloseBlocked();
            return;
        }

        WindowBoundsStore.Save(sender.Position, sender.Size);
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkflowStage stage })
            ViewModel.GoToStageCommand.Execute(stage);
    }

    private void ExistingAccount_Checked(object sender, RoutedEventArgs e) => ViewModel.UseExistingAccount = true;

    private void ManualAccount_Checked(object sender, RoutedEventArgs e) => ViewModel.UseExistingAccount = false;

    private async void RebootRequested(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Reboot this computer?",
            Content = "Save any other work first. Windows will restart immediately, and the restored user can sign in after reboot.",
            PrimaryButtonText = "Reboot now",
            CloseButtonText = "Not yet",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.RebootNow();
    }
}
