using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class BackupSelectPrintersPage : Page
{
    private readonly NavigatorWindow _shell;

    public BackupSelectPrintersPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadPrinters();
    }

    private void LoadPrinters()
    {
        if (_shell.Session.AvailableBackupPrinters.Count == 0)
        {
            _shell.Session.AvailableBackupPrinters = _shell.PrinterDiscoveryService.GetPrinters().ToList();
        }

        PrintersListBox.ItemsSource = _shell.Session.AvailableBackupPrinters;
        PrintersSummaryText.Text = _shell.Session.AvailableBackupPrinters.Count switch
        {
            0 => "No installed printers were detected on this computer.",
            1 => "1 printer is available to save with the backup.",
            _ => $"{_shell.Session.AvailableBackupPrinters.Count} printers are available to save with the backup."
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupSelectUserPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _shell.Session.SelectedBackupPrinters = _shell.Session.AvailableBackupPrinters
            .Where(printer => printer.IsSelected)
            .Select(printer => new PrinterOption
            {
                Name = printer.Name,
                IsDefault = printer.IsDefault,
                IsSelected = printer.IsSelected
            })
            .ToList();

        _shell.GoToBackupSelectDestinationPage();
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
