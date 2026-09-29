using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class WorkflowCoordinator
{
    public WorkflowMode Mode { get; private set; }

    public WorkflowStage Stage { get; private set; } = WorkflowStage.Setup;

    public WorkflowStage HighestStage { get; private set; } = WorkflowStage.Setup;

    public bool IsLocked { get; private set; }

    public void Start(WorkflowMode mode)
    {
        if (mode == WorkflowMode.None) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        Stage = WorkflowStage.Setup;
        HighestStage = WorkflowStage.Setup;
        IsLocked = false;
    }

    public void Reset()
    {
        Mode = WorkflowMode.None;
        Stage = WorkflowStage.Setup;
        HighestStage = WorkflowStage.Setup;
        IsLocked = false;
    }

    public bool TryNavigate(WorkflowStage stage)
    {
        if (Mode == WorkflowMode.None || IsLocked || stage > HighestStage) return false;
        Stage = stage;
        return true;
    }

    public void ShowReview()
    {
        EnsureActive();
        Stage = WorkflowStage.Review;
        HighestStage = Max(HighestStage, WorkflowStage.Review);
    }

    public void BeginTransfer()
    {
        EnsureActive();
        Stage = WorkflowStage.Transfer;
        HighestStage = WorkflowStage.Transfer;
        IsLocked = true;
    }

    public void Complete()
    {
        EnsureActive();
        Stage = WorkflowStage.Finish;
        HighestStage = WorkflowStage.Finish;
        IsLocked = false;
    }

    public void ReturnToReviewAfterFailure()
    {
        EnsureActive();
        Stage = WorkflowStage.Review;
        HighestStage = WorkflowStage.Review;
        IsLocked = false;
    }

    public bool CanNavigateTo(WorkflowStage stage) =>
        Mode != WorkflowMode.None && !IsLocked && stage <= HighestStage;

    private void EnsureActive()
    {
        if (Mode == WorkflowMode.None) throw new InvalidOperationException("No migration workflow is active.");
    }

    private static WorkflowStage Max(WorkflowStage left, WorkflowStage right) => left >= right ? left : right;
}
