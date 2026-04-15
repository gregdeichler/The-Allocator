using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class RestoreSelectPrintersPage : Page
{
    private readonly NavigatorWindow _shell;

    public RestoreSelectPrintersPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadPrinters();
    }

    private void LoadPrinters()
    {
        foreach (var printer in _shell.Session.AvailableRestorePrinters)
        {
            if (_shell.Session.SelectedRestorePrinters.Count == 0)
            {
                printer.IsSelected = true;
            }
        }

        PrintersListBox.ItemsSource = _shell.Session.AvailableRestorePrinters;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToRestoreSelectTargetUserPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _shell.Session.SelectedRestorePrinters = _shell.Session.AvailableRestorePrinters
            .Where(printer => printer.IsSelected)
            .Select(printer => new PrinterOption
            {
                Name = printer.Name,
                IsDefault = printer.IsDefault,
                DriverName = printer.DriverName,
                PortName = printer.PortName,
                IsNetworkPrinter = printer.IsNetworkPrinter,
                ConnectionPath = printer.ConnectionPath,
                IsSelected = printer.IsSelected
            })
            .ToList();

        _shell.GoToRestoreReviewPage();
    }

    private void PrintersListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || PrintersListBox.SelectedItem is not PrinterOption printer)
        {
            return;
        }

        printer.IsSelected = !printer.IsSelected;
        PrintersListBox.Items.Refresh();
        e.Handled = true;
    }
}
