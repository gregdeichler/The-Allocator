using System.Windows;
using System.Windows.Controls;
using TheAllocator.Models;

namespace TheAllocator.Pages;

public partial class RestoreSelectTargetUserPage : Page
{
    private readonly NavigatorWindow _shell;

    public RestoreSelectTargetUserPage(NavigatorWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadProfiles();
        UseExistingAccountRadioButton.IsChecked = _shell.Session.RestoreUseExistingAccount;
        EnterAccountRadioButton.IsChecked = !_shell.Session.RestoreUseExistingAccount;
        DomainAccountCheckBox.IsChecked = _shell.Session.RestoreUseDomainAccount;
        TargetUserTextBox.Text = string.IsNullOrWhiteSpace(_shell.Session.RestoreTargetUser)
            ? _shell.Session.RestoreManifest?.UserName ?? string.Empty
            : _shell.Session.RestoreTargetUser;
        UpdateCanContinue();
        UpdateResolvedAccountText();
    }

    private void LoadProfiles()
    {
        var profiles = _shell.ProfileDiscoveryService.GetProfiles().ToList();
        ProfilesListBox.ItemsSource = profiles;

        if (_shell.Session.SelectedRestoreExistingProfile is not null)
        {
            ProfilesListBox.SelectedItem = profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfilePath, _shell.Session.SelectedRestoreExistingProfile.ProfilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is ProfileOption profile)
        {
            UseExistingAccountRadioButton.IsChecked = true;
            TargetUserTextBox.Text = profile.UserName;
        }
    }

    private void TargetUserTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCanContinue();
        UpdateResolvedAccountText();
    }

    private void AccountMode_Checked(object sender, RoutedEventArgs e)
    {
        UpdateCanContinue();
        UpdateResolvedAccountText();
    }

    private void DomainAccountCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateResolvedAccountText();
    }

    private void UpdateCanContinue()
    {
        if (UseExistingAccountRadioButton is null || ProfilesListBox is null || NextButton is null || TargetUserTextBox is null)
        {
            return;
        }

        if (UseExistingAccountRadioButton.IsChecked == true)
        {
            NextButton.IsEnabled = ProfilesListBox.SelectedItem is ProfileOption;
            return;
        }

        NextButton.IsEnabled = !string.IsNullOrWhiteSpace(TargetUserTextBox.Text);
    }

    private void UpdateResolvedAccountText()
    {
        if (TargetUserTextBox is null || DomainAccountCheckBox is null || ResolvedAccountText is null)
        {
            return;
        }

        var rawUserName = TargetUserTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawUserName))
        {
            ResolvedAccountText.Text = "Final sign-in account: not selected yet";
            return;
        }

        var resolved = DomainAccountCheckBox.IsChecked == true
            ? $"{rawUserName}@ad.vassar.edu"
            : rawUserName;

        ResolvedAccountText.Text = $"Final sign-in account: {resolved}";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoToRestoreSelectBackupFilePage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var useExisting = UseExistingAccountRadioButton.IsChecked == true;
        var selectedProfile = useExisting ? ProfilesListBox.SelectedItem as ProfileOption : null;
        var rawUserName = selectedProfile is not null
            ? selectedProfile.UserName
            : TargetUserTextBox.Text.Trim();

        var useDomain = DomainAccountCheckBox.IsChecked == true;

        _shell.Session.RestoreUseExistingAccount = useExisting;
        _shell.Session.RestoreUseDomainAccount = useDomain;
        _shell.Session.RestoreTargetUser = rawUserName;
        _shell.Session.SelectedRestoreExistingProfile = selectedProfile;
        _shell.Session.RestoreTargetAccountDisplay = useDomain
            ? $"{rawUserName}@ad.vassar.edu"
            : rawUserName;

        _shell.GoToRestoreSelectPrintersPage();
    }
}
