using System.Windows;
using System.Windows.Controls;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class BackupSelectUserPage : Page
{
    private readonly NavigatorWindow _shell;

    public BackupSelectUserPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        var profiles = _shell.ProfileDiscoveryService.GetProfiles();
        ProfilesListBox.ItemsSource = profiles;
        ProfileSummaryText.Text = profiles.Count switch
        {
            0 => "No backup-eligible user profiles were found on this computer yet.",
            1 => "1 user profile is ready to back up.",
            _ => $"{profiles.Count} user profiles are ready to back up."
        };

        if (_shell.Session.SelectedBackupProfile is not null)
        {
            ProfilesListBox.SelectedItem = profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfilePath, _shell.Session.SelectedBackupProfile.ProfilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _shell.Session.SelectedBackupProfile = ProfilesListBox.SelectedItem as ProfileOption;
        NextButton.IsEnabled = _shell.Session.SelectedBackupProfile is not null;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToStartPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToBackupSelectPrintersPage();
    }
}
