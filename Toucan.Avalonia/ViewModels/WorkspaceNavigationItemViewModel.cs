using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Explorer state around an immutable Core navigation node.</summary>
public sealed partial class WorkspaceNavigationItemViewModel(WorkspaceNavigationNode node) : ObservableObject
{
    public WorkspaceNavigationNode Node { get; } = node;
    public string Id => Node.Id;
    public string Label => Node.Label;
    public string OriginsText => string.Join(", ", Node.Origins);
    public bool HasChildren => Children.Count > 0;
    public bool IsEntry => Node.Kind == WorkspaceNavigationNodeKind.Entry;
    public IReadOnlyList<WorkspaceNavigationItemViewModel> Children { get; } = node.Children.Select(n => new WorkspaceNavigationItemViewModel(n)).ToArray();
    [ObservableProperty] private bool isExpanded;
}
