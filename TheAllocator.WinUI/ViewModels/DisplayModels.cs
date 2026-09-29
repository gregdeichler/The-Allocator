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
    public partial string StateLabel { get; set; } = "Upcoming";

    [ObservableProperty]
    public partial string Glyph { get; set; } = "\uE915";

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }
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
