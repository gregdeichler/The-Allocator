namespace TheAllocator.Models;

public enum WorkflowMode
{
    None,
    Backup,
    Restore
}

public enum WorkflowStage
{
    Setup,
    Review,
    Transfer,
    Finish
}

public enum OperationPhase
{
    Preparing,
    Validating,
    Archiving,
    Extracting,
    Metadata,
    Printers,
    Verifying,
    Complete,
    Failed
}

public enum OperationSeverity
{
    Information,
    Success,
    Warning,
    Error
}

public enum MilestoneState
{
    Pending,
    Active,
    Complete,
    Failed
}

public sealed record OperationProgress(
    WorkflowMode Operation,
    OperationPhase Phase,
    string Headline,
    string Detail = "",
    double? Fraction = null,
    MilestoneState Milestone = MilestoneState.Active,
    OperationSeverity Severity = OperationSeverity.Information);

public enum PreflightStatus
{
    Passed,
    Warning,
    Blocked
}

public sealed record PreflightCheck(
    string Id,
    string Title,
    string Detail,
    PreflightStatus Status,
    string? ActionLabel = null);
