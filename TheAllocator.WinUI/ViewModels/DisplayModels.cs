using CommunityToolkit.Mvvm.ComponentModel;
using TheAllocator.Models;

namespace TheAllocator.WinUI.ViewModels;

public sealed partial class WorkflowStepItem : ObservableObject
{
    public WorkflowStepItem(WorkflowStage stage, string title)
    {
        Stage = stage;
        Title = title;
    }

    public WorkflowStage Stage { get; }

    public string Title { get; }

    [ObservableProperty]
    private string stateLabel = "Upcoming";

    [ObservableProperty]
    private string glyph = "\uE915";

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private bool isAvailable;
}

public sealed record SummaryRow(string Label, string Value);

public sealed record ProgressMilestone(string Title, string Detail, MilestoneState State)
{
    public string Glyph => State switch
    {
        MilestoneState.Complete => "\uE73E",
        MilestoneState.Active => "\uE895",
        MilestoneState.Failed => "\uEA39",
        _ => "\uE915"
    };

    public string StateLabel => State.ToString();
}
