using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.Avalonia.ViewModels;

public sealed class TranslationFocusEventArgs(TranslationItemViewModel item) : EventArgs
{
    public TranslationItemViewModel Item { get; } = item;
}

public sealed record ContributedToolbarAction(string Title, System.Windows.Input.ICommand Command);

public partial class MainWindowViewModel
{
    private readonly Toucan.Core.Plugins.INotificationCenter _notifications;
    public NotificationHistoryViewModel NotificationHistory { get; }
    public IReadOnlyList<ContributedToolbarAction> ContributedToolbarActions => Services.KeybindingService.Registry.Commands
        .Where(c => c.PluginId != null && c.Definition.Placements.HasFlag(Toucan.Plugins.CommandPlacement.Toolbar)
            && Services.KeybindingService.Registry.GetState(c.Id).Availability != Toucan.Plugins.CommandAvailability.Hidden)
        .Select(c => new ContributedToolbarAction(Services.KeybindingService.Registry.GetTitle(c.Id),
            Services.RegistryCommand.For(Services.KeybindingService.Registry, c.Id))).ToList();

    internal void RefreshContributedToolbar() => OnPropertyChanged(nameof(ContributedToolbarActions));

    private void OnNotificationHistoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationHistory.Count))
            Services.StatusBarService.Instance.ViewModel?.Notifications.Update(NotificationHistory.Count);
    }

    private void NotifyOutcome(string title, string message, Toucan.Plugins.NotificationSeverity severity,
        string? action = null, string? label = null) => _notifications.Publish("toucan", new Toucan.Plugins.PluginNotification
        { Title = title, Message = message, Severity = severity, ActionCommandId = action, ActionLabel = label });

    [RelayCommand]
    private void ShowIssues() => Toucan.Core.Services.SidePanelRegistry.Instance.Activate("issues");
    public bool WorkflowGuideVisible => !AppOptions.WorkflowGuideDismissed;

    [RelayCommand]
    private void DismissWorkflowGuide()
    {
        AppOptions.WorkflowGuideDismissed = true; _preferenceService.Save(AppOptions);
        OnPropertyChanged(nameof(WorkflowGuideVisible));
    }

    [RelayCommand]
    private void ShowWorkflowGuide()
    {
        AppOptions.WorkflowGuideDismissed = false; _preferenceService.Save(AppOptions);
        OnPropertyChanged(nameof(WorkflowGuideVisible));
    }
    public event EventHandler<TranslationFocusEventArgs>? TranslationFocusRequested;

    public string FocusedTextCounts => FocusedTranslationItem is { } item
        ? $"{item.Language} · {item.CharacterCount} {Locales.Loc.T("characters")} · {item.WordCount} {Locales.Loc.T("words")}" : string.Empty;

    public string SuggestionProvenance => FocusedTranslationItem?.HasGhostText == true
        ? Locales.Loc.T("Suggestion from translation memory · Tab to accept") : string.Empty;

    private void RefreshFocusedActions()
    {
        OnPropertyChanged(nameof(FocusedTextCounts));
        OnPropertyChanged(nameof(SuggestionProvenance));
        ApproveAndNextCommand.NotifyCanExecuteChanged();
        AcceptSuggestionCommand.NotifyCanExecuteChanged();
    }

    private void OnFocusedItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshFocusedActions();

    private bool CanApproveFocused() => !IsAuditMode && ((FocusedEditorMode && FocusedTranslationItem == null && ZenCurrentItem != null) || FocusedTranslationItem is { IsEmpty: false, IsSource: false } item
        && item.Model.Permissions.EditMetadata && AllTranslation.Contains(item.Model));

    [RelayCommand(CanExecute = nameof(CanApproveFocused))]
    private void ApproveAndNext()
    {
        FlushPendingEdits();
        if (!CanApproveFocused()) return;
        if (FocusedTranslationItem == null) { ApproveCurrentCardAndNext(); return; }
        var item = FocusedTranslationItem!;
        ApproveItems([item.Model]);
        if (item.Model.IsApproved) NextPendingTranslation();
    }

    private bool CanAcceptSuggestion() => !IsAuditMode && FocusedTranslationItem is { HasGhostText: true, IsResourceReadOnly: false } item
        && AllTranslation.Contains(item.Model);

    [RelayCommand(CanExecute = nameof(CanAcceptSuggestion))]
    private void AcceptSuggestion()
    {
        if (FocusedTranslationItem is { } item && CanAcceptSuggestion())
        {
            AcceptGhostText(item);
            item.Flush();
            TranslationFocusRequested?.Invoke(this, new(item));
        }
    }

    [RelayCommand]
    private void NextPendingTranslation()
    {
        FlushPendingEdits();
        var language = FocusedTranslationItem?.IsSource == false ? FocusedTranslationItem.Language : null;
        var candidates = PagingController.Data.SelectMany(g => g.AllItems)
            .Where(i => !i.IsSource && !i.IsResourceReadOnly && (language == null || i.Language == language)
                && LanguageVisibilityFilter.All(f => f.Language != i.Language || f.IsVisible)).ToList();
        var index = candidates.IndexOf(FocusedTranslationItem!);
        var next = candidates.Skip(index + 1).Concat(candidates.Take(Math.Max(0, index)))
            .FirstOrDefault(i => !i.IsApproved);
        if (next == null) { StatusText = Locales.Loc.T("No more pending translations in this view."); return; }
        var group = PagingController.Data.First(g => g.AllItems.Contains(next));
        var groupIndex = PagingController.Data.IndexOf(group);
        SelectedGroup = group;
        FocusedIndex = groupIndex;
        if (!FocusedEditorMode && !ZenMode)
        {
            PagingController.GoTo(groupIndex / Math.Max(1, PagingController.PageSize) + 1);
            PagedUpdates();
        }
        FocusedTranslationItem = next;
        TranslationFocusRequested?.Invoke(this, new(next));
    }
}
