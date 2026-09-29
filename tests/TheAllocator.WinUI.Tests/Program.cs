using TheAllocator.Models;
using TheAllocator.Services;
using TheAllocator.WinUI.Infrastructure;
using TheAllocator.WinUI.ViewModels;

var tests = new (string Name, Func<Task> Body)[]
{
    ("backup setup reaches structured review", BackupSetupReachesReview),
    ("setup selections survive review navigation", SetupStateSurvivesBackNavigation),
    ("empty profile state is actionable", EmptyProfilesShowNotice),
    ("picker cancellation keeps restore setup unchanged", PickerCancellationIsSafe),
    ("invalid package inspection shows inline recovery", PackageInspectionFailureIsInline),
    ("valid package inspection populates restore state", PackageInspectionPopulatesState),
    ("blocked preflight disables transfer", BlockedPreflightDisablesTransfer),
    ("entered setup requires discard confirmation", EnteredSetupRequiresConfirmation)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Body();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"WinUI view-model tests: {tests.Length - failures.Count} passed, {failures.Count} failed.");
if (failures.Count > 0) Environment.ExitCode = 1;

static Task BackupSetupReachesReview()
{
    var services = FakeServices.WithProfile();
    var viewModel = new MainViewModel(services);
    viewModel.StartBackupCommand.Execute(null);
    viewModel.SelectedBackupProfile = viewModel.BackupProfiles[0];
    viewModel.BackupDestination = Path.GetTempPath();
    True(viewModel.CanContinue);
    viewModel.ContinueToReviewCommand.Execute(null);
    Equal(WorkflowStage.Review, viewModel.Stage);
    True(viewModel.SummaryRows.Count >= 4);
    True(viewModel.PreflightChecks.Count >= 3);
    return Task.CompletedTask;
}

static Task SetupStateSurvivesBackNavigation()
{
    var viewModel = new MainViewModel(FakeServices.WithProfile());
    viewModel.StartBackupCommand.Execute(null);
    viewModel.SelectedBackupProfile = viewModel.BackupProfiles[0];
    viewModel.BackupDestination = Path.GetTempPath();
    viewModel.ContinueToReviewCommand.Execute(null);
    viewModel.BackCommand.Execute(null);
    Equal(WorkflowStage.Setup, viewModel.Stage);
    Equal(Path.GetTempPath(), viewModel.BackupDestination);
    True(viewModel.SelectedBackupProfile is not null);
    return Task.CompletedTask;
}

static Task EmptyProfilesShowNotice()
{
    var viewModel = new MainViewModel(FakeServices.Empty());
    viewModel.StartBackupCommand.Execute(null);
    True(viewModel.IsNoticeOpen);
    Equal("No profiles found", viewModel.NoticeTitle);
    return Task.CompletedTask;
}

static async Task PickerCancellationIsSafe()
{
    var services = FakeServices.Empty();
    services.Picker.PackagePath = null;
    var viewModel = new MainViewModel(services);
    viewModel.StartRestoreCommand.Execute(null);
    await viewModel.BrowsePackageCommand.ExecuteAsync(null);
    False(viewModel.IsPackageLoaded);
    Equal(string.Empty, viewModel.RestorePackagePath);
}

static async Task PackageInspectionPopulatesState()
{
    var services = FakeServices.Empty();
    services.Picker.PackagePath = @"C:\Backups\alex-backup.7z";
    services.Restore.Package = new RestorePackageInfo
    {
        Success = true,
        Manifest = new BackupManifest
        {
            UserName = "alex",
            DisplayName = "Alex",
            SourceComputerName = "OLD-PC",
            SourceOperatingSystem = "Windows 11",
            CreatedAt = new DateTime(2026, 9, 29, 10, 0, 0)
        },
        Printers = [new BackupPrinterInfo { Name = "Main Office" }]
    };
    var viewModel = new MainViewModel(services);
    viewModel.StartRestoreCommand.Execute(null);
    await viewModel.BrowsePackageCommand.ExecuteAsync(null);
    True(viewModel.IsPackageLoaded);
    Equal("alex", viewModel.ManualTargetUser);
    Equal(1, viewModel.RestorePrinters.Count);
}

static async Task PackageInspectionFailureIsInline()
{
    var services = FakeServices.Empty();
    services.Picker.PackagePath = @"C:\Backups\broken.7z";
    services.Restore.Package = new RestorePackageInfo { ErrorMessage = "Archive metadata is missing." };
    var viewModel = new MainViewModel(services);
    viewModel.StartRestoreCommand.Execute(null);
    await viewModel.BrowsePackageCommand.ExecuteAsync(null);
    False(viewModel.IsPackageLoaded);
    True(viewModel.IsNoticeOpen);
    Equal("Backup package problem", viewModel.NoticeTitle);
}

static Task BlockedPreflightDisablesTransfer()
{
    var viewModel = new MainViewModel(FakeServices.WithProfile());
    viewModel.StartBackupCommand.Execute(null);
    viewModel.SelectedBackupProfile = viewModel.BackupProfiles[0];
    viewModel.BackupDestination = Path.GetTempPath();
    viewModel.ContinueToReviewCommand.Execute(null);
    False(viewModel.CanBeginTransfer);
    True(viewModel.PreflightChecks.Any(check => check.Status == PreflightStatus.Blocked));
    return Task.CompletedTask;
}

static Task EnteredSetupRequiresConfirmation()
{
    var viewModel = new MainViewModel(FakeServices.WithProfile());
    var confirmationRequested = false;
    viewModel.AbandonSetupRequested += (_, _) => confirmationRequested = true;
    viewModel.StartBackupCommand.Execute(null);
    viewModel.SelectedBackupProfile = viewModel.BackupProfiles[0];
    viewModel.StartOverCommand.Execute(null);
    True(confirmationRequested);
    Equal(WorkflowMode.Backup, viewModel.Mode);
    viewModel.ConfirmStartOver();
    Equal(WorkflowMode.None, viewModel.Mode);
    return Task.CompletedTask;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}

static void True(bool value)
{
    if (!value) throw new Exception("Expected true, got false.");
}

static void False(bool value) => True(!value);

sealed class FakeServices : IAppServices
{
    private FakeServices(IReadOnlyList<ProfileOption> profiles)
    {
        Profiles = new FakeProfiles(profiles);
        Printers = new FakePrinters();
        MachineInfo = new FakeMachineInfo();
        Backup = new FakeBackup();
        Restore = new FakeRestore();
        SevenZip = new SevenZipService(Path.GetTempPath());
        WindowsProfiles = new WindowsProfileService();
        Pickers = Picker = new FakePicker();
        Shell = new FakeShell();
    }

    public static FakeServices WithProfile() => new([
        new ProfileOption { UserName = "alex", DisplayName = "Alex", ProfilePath = @"C:\Users\alex", Sid = "S-1-5-21-1" }
    ]);

    public static FakeServices Empty() => new([]);

    public IProfileDiscoveryService Profiles { get; }
    public IPrinterDiscoveryService Printers { get; }
    public IMachineInfoService MachineInfo { get; }
    public IBackupService Backup { get; }
    IRestoreService IAppServices.Restore => Restore;
    public SevenZipService SevenZip { get; }
    public WindowsProfileService WindowsProfiles { get; }
    public IFilePickerService Pickers { get; }
    public IShellActionService Shell { get; }
    public FakePicker Picker { get; }
    public FakeRestore Restore { get; }

    private sealed class FakeProfiles(IReadOnlyList<ProfileOption> profiles) : IProfileDiscoveryService
    {
        public IReadOnlyList<ProfileOption> GetProfiles() => profiles;
    }

    private sealed class FakePrinters : IPrinterDiscoveryService
    {
        public IReadOnlyList<PrinterOption> GetPrinters() => [];
        public IReadOnlyList<BackupPrinterInfo> GetPrinterDetails(IEnumerable<PrinterOption> selectedPrinters) => [];
    }

    private sealed class FakeMachineInfo : IMachineInfoService
    {
        public MachineInfoSnapshot GetSnapshot() => new() { DeviceName = "TEST-PC", Model = "Virtual machine", MemorySummary = "16 GB RAM", StorageSummary = "256 GB storage" };
        public string OperatingSystemDisplayName => "Windows 11";
        public string OperatingSystemVersion => "10.0.26100";
    }

    private sealed class FakeBackup : IBackupService
    {
        public Task<BackupResult> CreateBackupAsync(AllocatorSession session, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackupResult { Success = true });
    }

    public sealed class FakeRestore : IRestoreService
    {
        public RestorePackageInfo Package { get; set; } = new();
        public Task<RestorePackageInfo> InspectPackageAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Package);
        public Task<RestoreResult> RestoreAsync(AllocatorSession session, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RestoreResult { Success = true });
    }

    public sealed class FakePicker : IFilePickerService
    {
        public string? FolderPath { get; set; }
        public string? PackagePath { get; set; }
        public Task<string?> PickFolderAsync() => Task.FromResult(FolderPath);
        public Task<string?> PickBackupPackageAsync() => Task.FromResult(PackagePath);
    }

    private sealed class FakeShell : IShellActionService
    {
        public void OpenFolder(string path) { }
        public void OpenLog(string path) { }
        public void RebootComputer() { }
    }
}
