using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One step of the feature tour.</summary>
public sealed record FeatureTourStep(string Title, string Body);

/// <summary>
/// A dismissible, one-time tour that points out the editor, panels and command palette after first-run setup.
/// It is shown once; <see cref="Toucan.Core.Options.AppOptions.FeatureTourSeen"/> records that it has been seen.
/// </summary>
public partial class FeatureTourViewModel : ObservableObject
{
    private readonly Action _onFinished;

    public FeatureTourViewModel(Action onFinished, IReadOnlyList<FeatureTourStep>? steps = null)
    {
        _onFinished = onFinished;
        Steps = steps ?? Default;
    }

    public static IReadOnlyList<FeatureTourStep> Default { get; } =
    [
        new("Open a project", "Open a folder of translation files, or start from a framework template. Everything you do stays local until you save."),
        new("Edit side by side", "Each key shows every language. The source value and a character and word count sit under the field you are editing."),
        new("Review by keyboard", "Approve and Next (Mod+Enter) approves the current values and jumps to the next item that needs review, without leaving the keyboard."),
        new("Find anything", "The command palette (Mod+Shift+P) runs every command, and the activity bar on the left opens the panels: search, issues, languages and the inspector."),
    ];

    public IReadOnlyList<FeatureTourStep> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep), nameof(IsLastStep), nameof(PositionText))]
    private int index;

    [ObservableProperty] private bool isVisible;

    public FeatureTourStep CurrentStep => Steps[Math.Clamp(Index, 0, Steps.Count - 1)];

    public bool IsLastStep => Index >= Steps.Count - 1;

    public string PositionText => $"{Index + 1} / {Steps.Count}";

    public void Show()
    {
        Index = 0;
        IsVisible = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep) Finish();
        else Index++;
    }

    [RelayCommand]
    private void Skip() => Finish();

    private void Finish()
    {
        IsVisible = false;
        _onFinished();
    }
}
