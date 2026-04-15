using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using TheAllocator.Models;
using TheAllocator.Pages;
using TheAllocator.Services;

namespace TheAllocator;

public partial class NavigatorWindow : Window
{
    private static readonly string[] StartSteps =
    [
        "Choose Computer"
    ];

    private static readonly string[] BackupSteps =
    [
        "Select User",
        "Select Printers",
        "Select Destination",
        "Review and Start",
        "Backup Progress",
        "Backup Complete"
    ];

    private static readonly string[] RestoreSteps =
    [
        "Select Backup File",
        "Choose Account",
        "Select Printers",
        "Review and Start",
        "Restore Progress",
        "Restore Complete"
    ];

    public NavigatorWindow()
    {
        InitializeComponent();
        LoadWordmark();
        Session = new AllocatorSession();
        MachineInfoService = new MachineInfoService();
        ProfileDiscoveryService = new ProfileDiscoveryService();
        PrinterDiscoveryService = new PrinterDiscoveryService();
        SevenZipService = new SevenZipService(AppContext.BaseDirectory);
        BackupService = new BackupService(PrinterDiscoveryService, SevenZipService);
        RestoreService = new RestoreService(SevenZipService);
        LoadMachineSummary();
        GoToStartPage();
    }

    public AllocatorSession Session { get; }

    public MachineInfoService MachineInfoService { get; }

    public ProfileDiscoveryService ProfileDiscoveryService { get; }

    public PrinterDiscoveryService PrinterDiscoveryService { get; }

    public SevenZipService SevenZipService { get; }

    public BackupService BackupService { get; }

    public RestoreService RestoreService { get; }

    public void GoToStartPage()
    {
        Navigate(
            new StartPage(this),
            workflowTitle: "Start",
            workflowSummary: "Choose whether you are on the old computer or the new computer.",
            steps: StartSteps,
            currentStepIndex: 0);
    }

    public void GoToBackupSelectUserPage()
    {
        NavigateBackup(new BackupSelectUserPage(this), 0);
    }

    public void GoToBackupSelectPrintersPage()
    {
        NavigateBackup(new BackupSelectPrintersPage(this), 1);
    }

    public void GoToBackupSelectDestinationPage()
    {
        NavigateBackup(new BackupSelectDestinationPage(this), 2);
    }

    public void GoToBackupReviewPage()
    {
        NavigateBackup(new BackupReviewPage(this), 3);
    }

    public void GoToBackupProgressPage()
    {
        NavigateBackup(new BackupProgressPage(this), 4);
    }

    public void GoToBackupCompletePage()
    {
        NavigateBackup(new BackupCompletePage(this), 5);
    }

    public void GoToRestoreSelectBackupFilePage()
    {
        NavigateRestore(new RestoreSelectBackupFilePage(this), 0);
    }

    public void GoToRestoreSelectTargetUserPage()
    {
        NavigateRestore(new RestoreSelectTargetUserPage(this), 1);
    }

    public void GoToRestoreSelectPrintersPage()
    {
        NavigateRestore(new RestoreSelectPrintersPage(this), 2);
    }

    public void GoToRestoreReviewPage()
    {
        NavigateRestore(new RestoreReviewPage(this), 3);
    }

    public void GoToRestoreProgressPage()
    {
        NavigateRestore(new RestoreProgressPage(this), 4);
    }

    public void GoToRestoreCompletePage()
    {
        NavigateRestore(new RestoreCompletePage(this), 5);
    }

    private void Navigate(Page page, string workflowTitle, string workflowSummary, IReadOnlyList<string> steps, int currentStepIndex)
    {
        WorkflowTitleText.Text = workflowTitle;
        WorkflowSummaryText.Text = workflowSummary;
        BackToStartButton.Visibility = currentStepIndex >= 0 && steps != StartSteps
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderWorkflowSteps(steps, currentStepIndex);
        ShellFrame.Navigate(page);
    }

    private void NavigateBackup(Page page, int currentStepIndex) =>
        Navigate(
            page,
            "Old Computer",
            "Follow the backup path from user selection through the finished archive.",
            BackupSteps,
            currentStepIndex);

    private void NavigateRestore(Page page, int currentStepIndex) =>
        Navigate(
            page,
            "New Computer",
            "Follow the restore path from package selection through the completed restore.",
            RestoreSteps,
            currentStepIndex);

    private void RenderWorkflowSteps(IReadOnlyList<string> steps, int currentStepIndex)
    {
        WorkflowStepsPanel.Children.Clear();
        Border? currentStepBorder = null;

        for (var index = 0; index < steps.Count; index++)
        {
            var isCurrent = index == currentStepIndex;
            var isComplete = index < currentStepIndex;

            var border = new Border
            {
                Margin = new Thickness(0, index == 0 ? 0 : 12, 0, 0),
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                Background = isCurrent
                    ? new SolidColorBrush(MediaColor.FromRgb(149, 24, 41))
                    : isComplete
                        ? new SolidColorBrush(MediaColor.FromRgb(255, 248, 239))
                        : new SolidColorBrush(MediaColor.FromRgb(250, 242, 236)),
                BorderBrush = isCurrent
                    ? new SolidColorBrush(MediaColor.FromRgb(149, 24, 41))
                    : new SolidColorBrush(MediaColor.FromRgb(208, 208, 206))
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                VerticalAlignment = VerticalAlignment.Top,
                Background = isCurrent
                    ? new SolidColorBrush(MediaColor.FromRgb(255, 248, 239))
                    : isComplete
                        ? new SolidColorBrush(MediaColor.FromRgb(149, 24, 41))
                        : new SolidColorBrush(MediaColor.FromRgb(233, 223, 216))
            };

            badge.Child = new TextBlock
            {
                Text = isComplete ? "?" : (index + 1).ToString(),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = isCurrent
                    ? new SolidColorBrush(MediaColor.FromRgb(149, 24, 41))
                    : isComplete
                        ? MediaBrushes.White
                        : new SolidColorBrush(MediaColor.FromRgb(99, 102, 106)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var textStack = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0)
            };

            textStack.Children.Add(new TextBlock
            {
                Text = steps[index],
                FontSize = 15,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isCurrent ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(32, 26, 28))
            });

            textStack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 0),
                Text = isCurrent ? "Current step" : isComplete ? "Completed" : "Upcoming",
                FontSize = 12,
                Foreground = isCurrent ? new SolidColorBrush(MediaColor.FromRgb(255, 240, 230)) : new SolidColorBrush(MediaColor.FromRgb(99, 102, 106))
            });

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(textStack, 1);
            row.Children.Add(badge);
            row.Children.Add(textStack);
            border.Child = row;
            WorkflowStepsPanel.Children.Add(border);

            if (isCurrent)
            {
                currentStepBorder = border;
            }
        }

        if (currentStepBorder is not null)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                currentStepBorder.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void GoToStartPage_Click(object sender, RoutedEventArgs e)
    {
        GoToStartPage();
    }

    private void LoadMachineSummary()
    {
        var snapshot = MachineInfoService.GetSnapshot();
        MachineNameText.Text = snapshot.DeviceName;
        MachineModelText.Text = snapshot.Model;
        MachineSpecsText.Text = $"{snapshot.MemorySummary} • {snapshot.StorageSummary}";
    }

    private void LoadWordmark()
    {
        try
        {
            var wordmarkPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vassar_Wordmark_VassarBurgundy_RGB.png");
            if (!File.Exists(wordmarkPath))
            {
                WordmarkImage.Visibility = Visibility.Collapsed;
                WordmarkFallbackText.Visibility = Visibility.Visible;
                return;
            }

            WordmarkImage.Source = new BitmapImage(new Uri(wordmarkPath, UriKind.Absolute));
        }
        catch
        {
            WordmarkImage.Visibility = Visibility.Collapsed;
            WordmarkFallbackText.Visibility = Visibility.Visible;
        }
    }
}
