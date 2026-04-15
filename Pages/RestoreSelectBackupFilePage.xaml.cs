using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class RestoreSelectBackupFilePage : Page
{
    private readonly NavigatorWindow _shell;

    public RestoreSelectBackupFilePage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        BackupFileTextBox.Text = _shell.Session.RestorePackagePath;
        UpdateCanContinue();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Allocator Archives (*.allocator.7z;*.7z)|*.allocator.7z;*.7z|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            BackupFileTextBox.Text = dialog.FileName;
            UpdateCanContinue();
        }
    }

    private void UpdateCanContinue()
    {
        NextButton.IsEnabled = !string.IsNullOrWhiteSpace(BackupFileTextBox.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToStartPage();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        var packagePath = BackupFileTextBox.Text.Trim();
        NextButton.IsEnabled = false;

        var packageInfo = await _shell.RestoreService.InspectPackageAsync(packagePath);
        if (!packageInfo.Success || packageInfo.Manifest is null)
        {
            System.Windows.MessageBox.Show(
                string.IsNullOrWhiteSpace(packageInfo.ErrorMessage) ? "The backup package could not be read." : packageInfo.ErrorMessage,
                "Restore Package Problem",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UpdateCanContinue();
            return;
        }

        _shell.Session.RestorePackagePath = packagePath;
        _shell.Session.RestoreManifest = packageInfo.Manifest;
        _shell.Session.RestoreMetadataPath = packageInfo.MetadataPath;
        _shell.Session.RestorePrintersPath = packageInfo.PrintersPath;
        _shell.Session.AvailableRestorePrinters = packageInfo.Printers
            .Select(printer => new PrinterOption
            {
                Name = printer.Name,
                IsDefault = printer.IsDefault,
                DriverName = printer.DriverName,
                PortName = printer.PortName,
                IsNetworkPrinter = printer.IsNetworkPrinter,
                ConnectionPath = printer.ConnectionPath,
                IsSelected = true
            })
            .ToList();
        _shell.Session.SelectedRestorePrinters = _shell.Session.AvailableRestorePrinters
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

        if (string.IsNullOrWhiteSpace(_shell.Session.RestoreTargetUser))
        {
            _shell.Session.RestoreTargetUser = packageInfo.Manifest.UserName;
        }

        if (string.IsNullOrWhiteSpace(_shell.Session.RestoreTargetAccountDisplay))
        {
            _shell.Session.RestoreTargetAccountDisplay = _shell.Session.RestoreUseDomainAccount
                ? $"{_shell.Session.RestoreTargetUser}@ad.vassar.edu"
                : _shell.Session.RestoreTargetUser;
        }

        _shell.GoToRestoreSelectTargetUserPage();
    }
}
