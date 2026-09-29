using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheAllocator.Models;
using TheAllocator.Services;
using TheAllocator.WinUI.Infrastructure;

namespace TheAllocator.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IProfileDiscoveryService _profiles;
    private readonly IPrinterDiscoveryService _printers;
    private readonly IMachineInfoService _machineInfo;
    private readonly IBackupService _backup;
    private readonly IRestoreService _restore;
    private readonly SevenZipService _sevenZip;
    private readonly WindowsProfileService _windowsProfiles;
    private readonly IFilePickerService _pickers;
    private readonly IShellActionService _shell;
    private readonly WorkflowCoordinator _coordinator = new();
    private AllocatorSession _session = new();
    private DateTime? _operationStartedAt;

    public MainViewModel(IAppServices services)
    {
        _profiles = services.Profiles;
        _printers = services.Printers;
        _machineInfo = services.MachineInfo;
        _backup = services.Backup;
        _restore = services.Restore;
        _sevenZip = services.SevenZip;
        _windowsProfiles = services.WindowsProfiles;
        _pickers = services.Pickers;
        _shell = services.Shell;

        var machine = _machineInfo.GetSnapshot();
        MachineName = machine.DeviceName;
        MachineDetail = $"{machine.Model} · {_machineInfo.OperatingSystemDisplayName} · {machine.MemorySummary} · {machine.StorageSummary}";
        VersionLabel = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0"}";
        RefreshSteps();
    }

    public event EventHandler? RebootRequested;

    public ObservableCollection<ProfileOption> BackupProfiles { get; } = [];
    public ObservableCollection<ProfileOption> RestoreProfiles { get; } = [];
    public ObservableCollection<PrinterOption> BackupPrinters { get; } = [];
    public ObservableCollection<PrinterOption> RestorePrinters { get; } = [];
    public ObservableCollection<WorkflowStepItem> Steps { get; } = [];
    public ObservableCollection<PreflightCheck> PreflightChecks { get; } = [];
    public ObservableCollection<SummaryRow> SummaryRows { get; } = [];
    public ObservableCollection<ProgressMilestone> ProgressMilestones { get; } = [];

    public string MachineName { get; }
    public string MachineDetail { get; }
    public string VersionLabel { get; }

    public WorkflowMode Mode => _coordinator.Mode;

    public WorkflowStage Stage => _coordinator.Stage;

    [ObservableProperty]
    private ProfileOption? selectedBackupProfile;

    [ObservableProperty]
    private ProfileOption? selectedRestoreProfile;

    [ObservableProperty]
    private string backupDestination = string.Empty;

    [ObservableProperty]
    private string restorePackagePath = string.Empty;

    [ObservableProperty]
    private string manualTargetUser = string.Empty;

    [ObservableProperty]
    private bool useExistingAccount;

    [ObservableProperty]
    private bool useDomainAccount = true;

    [ObservableProperty]
    private bool isPackageLoaded;

    [ObservableProperty]
    private string packageSummary = "Choose a backup package to inspect its source and contents.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isTransferActive;

    [ObservableProperty]
    private bool isNoticeOpen;

    [ObservableProperty]
    private string noticeTitle = string.Empty;

    [ObservableProperty]
    private string noticeMessage = string.Empty;

    [ObservableProperty]
    private OperationSeverity noticeSeverity = OperationSeverity.Information;

    [ObservableProperty]
    private string progressHeadline = "Preparing the migration";

    [ObservableProperty]
    private string progressDetail = "The Allocator is getting ready.";

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressIndeterminate = true;

    [ObservableProperty]
    private string elapsedText = "Elapsed time: 0:00";

    [ObservableProperty]
    private string finishTitle = "Migration complete";

    [ObservableProperty]
    private string finishMessage = string.Empty;

    [ObservableProperty]
    private string resultPath = string.Empty;

    [ObservableProperty]
    private string resultLogPath = string.Empty;

    public bool IsHomeVisible => Mode == WorkflowMode.None;
    public bool IsWorkflowVisible => Mode != WorkflowMode.None;
    public bool IsBackup => Mode == WorkflowMode.Backup;
    public bool IsRestore => Mode == WorkflowMode.Restore;
    public bool IsSetup => IsWorkflowVisible && Stage == WorkflowStage.Setup;
    public bool IsReview => IsWorkflowVisible && Stage == WorkflowStage.Review;
    public bool IsTransfer => IsWorkflowVisible && Stage == WorkflowStage.Transfer;
    public bool IsFinish => IsWorkflowVisible && Stage == WorkflowStage.Finish;
    public bool IsManualAccount => !UseExistingAccount;
    public bool CanContinue => Mode switch
    {
        WorkflowMode.Backup => SelectedBackupProfile is not null && !string.IsNullOrWhiteSpace(BackupDestination),
        WorkflowMode.Restore => IsPackageLoaded && ((UseExistingAccount && SelectedRestoreProfile is not null) ||
            (!UseExistingAccount && !string.IsNullOrWhiteSpace(ManualTargetUser))),
        _ => false
    };
    public bool CanBeginTransfer => PreflightChecks.Count > 0 && PreflightChecks.All(check => check.Status != PreflightStatus.Blocked);
    public bool CanNavigateBack => !IsTransferActive && Stage is WorkflowStage.Setup or WorkflowStage.Review;
    public bool ShowOpenFolderAction => IsBackup && IsFinish;
    public bool ShowRebootAction => IsRestore && IsFinish;
    public bool ShowOpenLogAction => IsFinish && !string.IsNullOrWhiteSpace(ResultLogPath);
    public string WorkflowName => IsBackup ? "Old computer backup" : "New computer restore";
    public string SetupTitle => IsBackup ? "Set up the backup" : "Set up the restore";
    public string SetupDescription => IsBackup
        ? "Choose the Windows profile, optional printers, and destination for this backup."
        : "Inspect the backup package, choose the sign-in account, and select any printers to recreate.";
    public string ReviewTitle => IsBackup ? "Review the backup plan" : "Review the restore plan";
    public string ReviewDescription => "Resolve any blocked checks before starting. Warnings can be reviewed without exposing raw logs.";
    public string TransferTitle => IsBackup ? "Creating the backup" : "Restoring the profile";

    partial void OnSelectedBackupProfileChanged(ProfileOption? value) => NotifyCanContinue();
    partial void OnSelectedRestoreProfileChanged(ProfileOption? value) => NotifyCanContinue();
    partial void OnBackupDestinationChanged(string value) => NotifyCanContinue();
    partial void OnManualTargetUserChanged(string value) => NotifyCanContinue();
    partial void OnUseExistingAccountChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualAccount));
        NotifyCanContinue();
    }
    partial void OnIsPackageLoadedChanged(bool value) => NotifyCanContinue();
    partial void OnResultLogPathChanged(string value) => OnPropertyChanged(nameof(ShowOpenLogAction));

    [RelayCommand]
    private void StartBackup()
    {
        ResetWorkflow(WorkflowMode.Backup);
        LoadBackupChoices();
    }

    [RelayCommand]
    private void StartRestore()
    {
        ResetWorkflow(WorkflowMode.Restore);
        LoadRestoreProfiles();
    }

    [RelayCommand]
    private void Back()
    {
        if (IsTransferActive) return;
        if (Stage == WorkflowStage.Review)
        {
            _coordinator.TryNavigate(WorkflowStage.Setup);
            SyncWorkflowState();
            return;
        }

        _coordinator.Reset();
        SyncWorkflowState();
        IsNoticeOpen = false;
    }

    [RelayCommand]
    private void GoToStage(WorkflowStage requestedStage)
    {
        if (IsTransferActive) return;
        if (requestedStage is WorkflowStage.Setup or WorkflowStage.Review && _coordinator.TryNavigate(requestedStage))
        {
            SyncWorkflowState();
        }
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        try
        {
            var selected = await _pickers.PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(selected)) BackupDestination = selected;
        }
        catch (Exception ex)
        {
            ShowNotice("Destination could not be opened", ex.Message, OperationSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task BrowsePackageAsync()
    {
        try
        {
            var selected = await _pickers.PickBackupPackageAsync();
            if (string.IsNullOrWhiteSpace(selected)) return;
            RestorePackagePath = selected;
            await InspectPackageAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("Backup package could not be opened", ex.Message, OperationSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task InspectPackageAsync()
    {
        IsBusy = true;
        IsNoticeOpen = false;
        IsPackageLoaded = false;
        try
        {
            var package = await _restore.InspectPackageAsync(RestorePackagePath.Trim());
            if (!package.Success || package.Manifest is null)
            {
                ShowNotice("Backup package problem", package.ErrorMessage, OperationSeverity.Error);
                PackageSummary = "The selected package could not be inspected.";
                return;
            }

            _session.RestorePackagePath = RestorePackagePath.Trim();
            _session.RestoreManifest = package.Manifest;
            _session.RestoreMetadataPath = package.MetadataPath;
            _session.RestorePrintersPath = package.PrintersPath;
            _session.AvailableRestorePrinters = package.Printers.Select(ToPrinterOption).ToList();
            _session.SelectedRestorePrinters = _session.AvailableRestorePrinters.Select(ClonePrinter).ToList();

            RestorePrinters.Clear();
            foreach (var printer in _session.AvailableRestorePrinters) RestorePrinters.Add(printer);

            ManualTargetUser = package.Manifest.UserName;
            PackageSummary = $"{package.Manifest.DisplayName} ({package.Manifest.UserName}) · {package.Manifest.SourceComputerName} · " +
                $"{package.Manifest.SourceOperatingSystem} · created {package.Manifest.CreatedAt:g}";
            IsPackageLoaded = true;
            ShowNotice("Package ready", "The backup metadata passed inspection.", OperationSeverity.Success);
        }
        catch (Exception ex)
        {
            PackageSummary = "The selected package could not be inspected.";
            ShowNotice("Backup package problem", ex.Message, OperationSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ContinueToReview()
    {
        if (!CanContinue) return;
        IsNoticeOpen = false;
        if (IsBackup) PrepareBackupReview(); else PrepareRestoreReview();
        _coordinator.ShowReview();
        SyncWorkflowState();
    }

    [RelayCommand]
    private async Task BeginTransferAsync()
    {
        if (!CanBeginTransfer || IsTransferActive) return;

        _coordinator.BeginTransfer();
        SyncWorkflowState();
        IsTransferActive = true;
        IsBusy = true;
        IsNoticeOpen = false;
        _operationStartedAt = DateTime.Now;
        ProgressMilestones.Clear();
        ProgressHeadline = "Preparing the migration";
        ProgressDetail = "The operation has started. Keep this computer connected to power and storage.";
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        var progress = new Progress<OperationProgress>(ApplyProgress);
        try
        {
            if (IsBackup) await RunBackupAsync(progress); else await RunRestoreAsync(progress);
        }
        finally
        {
            IsBusy = false;
            IsTransferActive = false;
            NotifyComputedProperties();
        }
    }

    [RelayCommand]
    private void StartOver()
    {
        _coordinator.Reset();
        _session = new AllocatorSession();
        IsNoticeOpen = false;
        SyncWorkflowState();
    }

    [RelayCommand]
    private void OpenResultFolder() => _shell.OpenFolder(ResultPath);

    [RelayCommand]
    private void OpenResultLog() => _shell.OpenLog(ResultLogPath);

    [RelayCommand]
    private void RequestReboot() => RebootRequested?.Invoke(this, EventArgs.Empty);

    public void RebootNow() => _shell.RebootComputer();

    public void ReportCloseBlocked() =>
        ShowNotice("Migration in progress", "Keep The Allocator open until the transfer finishes.", OperationSeverity.Warning);

    private void ResetWorkflow(WorkflowMode newMode)
    {
        _session = new AllocatorSession();
        _coordinator.Start(newMode);
        IsNoticeOpen = false;
        IsPackageLoaded = false;
        RestorePackagePath = string.Empty;
        BackupDestination = string.Empty;
        UseExistingAccount = false;
        UseDomainAccount = true;
        ManualTargetUser = string.Empty;
        SyncWorkflowState();
    }

    private void LoadBackupChoices()
    {
        BackupProfiles.Clear();
        foreach (var profile in _profiles.GetProfiles()) BackupProfiles.Add(profile);
        BackupPrinters.Clear();
        foreach (var printer in _printers.GetPrinters()) BackupPrinters.Add(printer);
        if (BackupProfiles.Count == 0)
            ShowNotice("No profiles found", "No eligible user profiles were found under C:\\Users.", OperationSeverity.Warning);
    }

    private void LoadRestoreProfiles()
    {
        RestoreProfiles.Clear();
        foreach (var profile in _profiles.GetProfiles()) RestoreProfiles.Add(profile);
    }

    private void PrepareBackupReview()
    {
        _session.SelectedBackupProfile = SelectedBackupProfile;
        _session.AvailableBackupPrinters = BackupPrinters.ToList();
        _session.SelectedBackupPrinters = BackupPrinters.Where(item => item.IsSelected).Select(ClonePrinter).ToList();
        _session.BackupDestinationFolder = BackupDestination.Trim();
        var userName = SelectedBackupProfile!.UserName;
        _session.BackupPackageName = _sevenZip.GetRecommendedArchiveName(userName);
        _session.BackupMetadataFileName = $"{userName}-backup.json";
        _session.BackupPrintersFileName = $"{userName}-printers.json";
        _session.BackupLogFileName = $"{userName}-backup-log.txt";

        SummaryRows.Clear();
        SummaryRows.Add(new("User", SelectedBackupProfile.DisplayName));
        SummaryRows.Add(new("Profile", SelectedBackupProfile.ProfilePath));
        SummaryRows.Add(new("Printers", $"{_session.SelectedBackupPrinters.Count} selected"));
        SummaryRows.Add(new("Destination", BackupDestination));
        SummaryRows.Add(new("Package", Path.Combine(BackupDestination, userName, _session.BackupPackageName)));

        PreflightChecks.Clear();
        PreflightChecks.Add(new("profile", "Source profile selected", SelectedBackupProfile.ProfilePath, PreflightStatus.Passed));
        PreflightChecks.Add(new("destination", "Destination selected", BackupDestination, Directory.Exists(BackupDestination) ? PreflightStatus.Passed : PreflightStatus.Blocked, "Choose destination"));
        PreflightChecks.Add(new("engine", "Archive engine", _sevenZip.IsAvailable ? "Integrated 7-Zip files are available." : "The integrated 7-Zip files are missing.", _sevenZip.IsAvailable ? PreflightStatus.Passed : PreflightStatus.Blocked));
        PreflightChecks.Add(new("printers", "Printer capture", _session.SelectedBackupPrinters.Count == 0 ? "No printers will be included." : $"{_session.SelectedBackupPrinters.Count} printer(s) will be recorded.", _session.SelectedBackupPrinters.Count == 0 ? PreflightStatus.Warning : PreflightStatus.Passed));
        OnPropertyChanged(nameof(CanBeginTransfer));
    }

    private void PrepareRestoreReview()
    {
        _session.RestoreUseExistingAccount = UseExistingAccount;
        _session.SelectedRestoreExistingProfile = UseExistingAccount ? SelectedRestoreProfile : null;
        _session.RestoreUseDomainAccount = UseDomainAccount;
        _session.RestoreTargetUser = UseExistingAccount ? SelectedRestoreProfile!.UserName : ManualTargetUser.Trim();
        _session.RestoreTargetAccountDisplay = UseDomainAccount ? $"{_session.RestoreTargetUser}@ad.vassar.edu" : _session.RestoreTargetUser;
        _session.SelectedRestorePrinters = RestorePrinters.Where(item => item.IsSelected).Select(ClonePrinter).ToList();

        var sourceUser = _session.RestoreManifest?.UserName ?? string.Empty;
        var crossUser = !string.Equals(NormalizeAccount(sourceUser), NormalizeAccount(_session.RestoreTargetUser), StringComparison.OrdinalIgnoreCase);
        var profilesRoot = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users");
        var targetPath = UseExistingAccount && SelectedRestoreProfile is not null
            ? SelectedRestoreProfile.ProfilePath
            : Path.Combine(profilesRoot, _session.RestoreTargetUser);
        var targetExists = Directory.Exists(targetPath);
        _session.RestoreCollisionMode = crossUser || targetExists
            ? RestoreCollisionMode.MergeIntoExistingProfile
            : RestoreCollisionMode.OverwriteExistingProfile;

        SummaryRows.Clear();
        SummaryRows.Add(new("Package", _session.RestorePackagePath));
        SummaryRows.Add(new("Source user", sourceUser));
        SummaryRows.Add(new("Target account", _session.RestoreTargetAccountDisplay));
        SummaryRows.Add(new("Target profile", targetPath));
        SummaryRows.Add(new("Restore approach", _session.RestoreCollisionMode == RestoreCollisionMode.MergeIntoExistingProfile ? "Preserve the existing Windows profile" : "Let Windows create a fresh profile"));
        SummaryRows.Add(new("Printers", $"{_session.SelectedRestorePrinters.Count} selected"));

        PreflightChecks.Clear();
        PreflightChecks.Add(new("package", "Backup package inspected", _session.RestorePackagePath, File.Exists(_session.RestorePackagePath) ? PreflightStatus.Passed : PreflightStatus.Blocked));
        PreflightChecks.Add(BuildIdentityCheck());
        PreflightChecks.Add(new("profile", "Target profile", targetExists ? "An existing profile folder will be preserved." : "Windows will create a fresh profile before files are restored.", crossUser && !targetExists ? PreflightStatus.Blocked : PreflightStatus.Passed));
        var sourceOs = _session.RestoreManifest?.SourceOperatingSystem ?? "Unknown Windows version";
        var differentOs = !sourceOs.Contains(_machineInfo.OperatingSystemDisplayName, StringComparison.OrdinalIgnoreCase);
        PreflightChecks.Add(new("windows", "Windows compatibility", $"Source: {sourceOs}. This PC: {_machineInfo.OperatingSystemDisplayName}.", differentOs ? PreflightStatus.Warning : PreflightStatus.Passed));
        PreflightChecks.Add(new("space", "Destination capacity", "Archive structure and exact destination capacity will be verified before profile changes begin.", PreflightStatus.Passed));
        OnPropertyChanged(nameof(CanBeginTransfer));
    }

    private PreflightCheck BuildIdentityCheck()
    {
        try
        {
            var identity = _windowsProfiles.ResolveRestoreIdentity(_session);
            return new("identity", "Target account resolved", $"{identity.AccountName} · {identity.Sid}", PreflightStatus.Passed);
        }
        catch (Exception ex)
        {
            return new("identity", "Target account could not be resolved", ex.Message, PreflightStatus.Blocked, "Edit account");
        }
    }

    private async Task RunBackupAsync(IProgress<OperationProgress> progress)
    {
        var userName = _session.SelectedBackupProfile!.UserName;
        _session.BackupPackagePath = Path.Combine(_session.BackupDestinationFolder, userName, _session.BackupPackageName);
        _session.BackupStartedAt = _operationStartedAt;
        _session.BackupJobId = Guid.NewGuid().ToString("N");
        var result = await _backup.CreateBackupAsync(_session, progress);
        _session.BackupCompletedAt = DateTime.Now;
        if (!result.Success)
        {
            CompleteWithFailure("Backup failed", result.ErrorMessage, result.LogPath);
            return;
        }

        _session.BackupArchiveSizeBytes = result.ArchiveSizeBytes;
        ResultPath = result.ArchivePath;
        ResultLogPath = result.LogPath;
        FinishTitle = "Backup complete";
        FinishMessage = $"The package is ready to move to the new computer. {result.CopiedFileCount:N0} files were archived ({SizeFormattingService.ToReadableSize(result.ArchiveSizeBytes)}).";
        CompleteSuccessfully();
    }

    private async Task RunRestoreAsync(IProgress<OperationProgress> progress)
    {
        _session.RestoreStartedAt = _operationStartedAt;
        _session.RestoreJobId = string.IsNullOrWhiteSpace(_session.RestoreManifest?.JobId) ? Guid.NewGuid().ToString("N") : _session.RestoreManifest.JobId;
        var result = await _restore.RestoreAsync(_session, progress);
        _session.RestoreCompletedAt = DateTime.Now;
        if (!result.Success)
        {
            CompleteWithFailure("Restore failed", result.ErrorMessage, result.RestoreLogPath);
            return;
        }

        _session.RestoreTargetProfilePath = result.TargetProfilePath;
        _session.RestoreCopiedFileCount = result.CopiedFileCount;
        ResultPath = result.TargetProfilePath;
        ResultLogPath = result.RestoreLogPath;
        FinishTitle = "Files restored";
        FinishMessage = $"{result.CopiedFileCount:N0} files were restored. Reboot this computer, then confirm that {_session.RestoreTargetAccountDisplay} can sign in and load the desktop.";
        CompleteSuccessfully();
    }

    private void ApplyProgress(OperationProgress update)
    {
        ProgressHeadline = update.Headline;
        ProgressDetail = update.Detail;
        IsProgressIndeterminate = update.Fraction is null;
        if (update.Fraction is not null) ProgressValue = Math.Clamp(update.Fraction.Value * 100, 0, 100);
        ElapsedText = $"Elapsed time: {FormatElapsed()}";

        var existing = ProgressMilestones.FirstOrDefault(item => item.Title == update.Headline);
        if (existing is null)
        {
            for (var index = 0; index < ProgressMilestones.Count; index++)
            {
                var old = ProgressMilestones[index];
                if (old.State == MilestoneState.Active)
                    ProgressMilestones[index] = old with { State = MilestoneState.Complete };
            }
            ProgressMilestones.Add(new(update.Headline, update.Detail, update.Milestone));
        }
        else
        {
            var index = ProgressMilestones.IndexOf(existing);
            ProgressMilestones[index] = new(update.Headline, update.Detail, update.Milestone);
        }
    }

    private void CompleteSuccessfully()
    {
        ProgressValue = 100;
        IsProgressIndeterminate = false;
        ElapsedText = $"Completed in {FormatElapsed()}";
        _coordinator.Complete();
        SyncWorkflowState();
    }

    private void CompleteWithFailure(string title, string message, string logPath)
    {
        ResultLogPath = logPath;
        ProgressMilestones.Add(new(title, message, MilestoneState.Failed));
        ShowNotice(title, message, OperationSeverity.Error);
        _coordinator.ReturnToReviewAfterFailure();
        SyncWorkflowState();
    }

    private void RefreshSteps()
    {
        Steps.Clear();
        foreach (var value in Enum.GetValues<WorkflowStage>())
        {
            var index = (int)value;
            var isCurrent = Mode != WorkflowMode.None && value == Stage;
            var isComplete = Mode != WorkflowMode.None && index < (int)Stage;
            Steps.Add(new WorkflowStepItem(value, value.ToString())
            {
                IsCurrent = isCurrent,
                IsAvailable = _coordinator.CanNavigateTo(value),
                StateLabel = isCurrent ? "Current" : isComplete ? "Complete" : "Upcoming",
                Glyph = isComplete ? "\uE73E" : isCurrent ? "\uE895" : "\uE915"
            });
        }
    }

    private void ShowNotice(string title, string message, OperationSeverity severity)
    {
        NoticeTitle = title;
        NoticeMessage = message;
        NoticeSeverity = severity;
        IsNoticeOpen = true;
    }

    private void NotifyCanContinue()
    {
        OnPropertyChanged(nameof(CanContinue));
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(IsHomeVisible));
        OnPropertyChanged(nameof(IsWorkflowVisible));
        OnPropertyChanged(nameof(IsBackup));
        OnPropertyChanged(nameof(IsRestore));
        OnPropertyChanged(nameof(IsSetup));
        OnPropertyChanged(nameof(IsReview));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(IsFinish));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(ShowOpenFolderAction));
        OnPropertyChanged(nameof(ShowRebootAction));
        OnPropertyChanged(nameof(WorkflowName));
        OnPropertyChanged(nameof(SetupTitle));
        OnPropertyChanged(nameof(SetupDescription));
        OnPropertyChanged(nameof(ReviewTitle));
        OnPropertyChanged(nameof(TransferTitle));
    }

    private void SyncWorkflowState()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Stage));
        RefreshSteps();
        NotifyComputedProperties();
    }

    private string FormatElapsed()
    {
        var elapsed = DateTime.Now - (_operationStartedAt ?? DateTime.Now);
        return elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
    }

    private static string NormalizeAccount(string value)
    {
        var normalized = value.Trim();
        var slash = normalized.LastIndexOf('\\');
        if (slash >= 0) normalized = normalized[(slash + 1)..];
        var at = normalized.IndexOf('@');
        if (at > 0) normalized = normalized[..at];
        return normalized;
    }

    private static PrinterOption ToPrinterOption(BackupPrinterInfo printer) => new()
    {
        Name = printer.Name,
        IsDefault = printer.IsDefault,
        DriverName = printer.DriverName,
        PortName = printer.PortName,
        HostAddress = printer.HostAddress,
        IsNetworkPrinter = printer.IsNetworkPrinter,
        ConnectionPath = printer.ConnectionPath,
        IsSelected = true
    };

    private static PrinterOption ClonePrinter(PrinterOption printer) => new()
    {
        Name = printer.Name,
        IsDefault = printer.IsDefault,
        DriverName = printer.DriverName,
        PortName = printer.PortName,
        HostAddress = printer.HostAddress,
        IsNetworkPrinter = printer.IsNetworkPrinter,
        ConnectionPath = printer.ConnectionPath,
        IsSelected = printer.IsSelected
    };
}
