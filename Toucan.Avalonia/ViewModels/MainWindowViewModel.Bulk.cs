using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Multi-select mode: select cards in the editor and act on all of them at once.</summary>
public partial class MainWindowViewModel
{
    [ObservableProperty] private bool isMultiSelectMode;

    public ObservableCollection<string> SelectedKeys { get; } = [];

    public int SelectionCount => SelectedKeys.Count;

    public string SelectionText => $"{SelectedKeys.Count} selected";

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        if (!value) BulkDeselectAll();
    }

    [RelayCommand]
    private void ToggleMultiSelect() => IsMultiSelectMode = !IsMultiSelectMode;

    /// <summary>Called by a card's checkbox.</summary>
    public void SetKeySelected(LanguageGroupViewModel group, bool selected)
    {
        var keys = KeysOf(group);
        foreach (var key in keys)
        {
            if (selected && !SelectedKeys.Contains(key)) SelectedKeys.Add(key);
            else if (!selected) SelectedKeys.Remove(key);
        }
        RaiseSelectionChanged();
    }

    private static IEnumerable<string> KeysOf(LanguageGroupViewModel group) =>
        group.IsPluralGroup ? group.PluralVariants.Select(v => v.FullNamespace) : [group.Namespace];

    [RelayCommand]
    private void BulkSelectAll()
    {
        SelectedKeys.Clear();
        foreach (var g in PagingController.Data)
        {
            g.IsSelectedForBulk = true;
            foreach (var k in KeysOf(g)) SelectedKeys.Add(k);
        }
        RaiseSelectionChanged();
    }

    [RelayCommand]
    private void BulkDeselectAll()
    {
        SelectedKeys.Clear();
        foreach (var g in PagingController.Data) g.IsSelectedForBulk = false;
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionText));
    }

    [RelayCommand]
    private async Task BulkDelete()
    {
        if (SelectedKeys.Count == 0) return;
        if (!await _messageService.ConfirmAsync($"Delete {SelectedKeys.Count} selected key(s) in every language?", "Delete Keys", "Delete", "Cancel")) return;
        var keys = SelectedKeys.ToList();
        BulkDeselectAll();
        DeleteNamespaces(keys);
    }

    [RelayCommand]
    private async Task BulkMoveToNamespace()
    {
        if (SelectedKeys.Count == 0 || !CanChangeKeys || IsAuditMode) return;
        var target = await _dialogService.ShowPromptAsync("Move to Namespace", "Enter the target key prefix (e.g. common.buttons). Leave empty to move to the root.");
        if (target == null) return;

        FlushPendingEdits();
        var keys = SelectedKeys.ToList();
        var prefix = target.Trim().TrimEnd('.');
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        var destinations = AllTranslation.Select(i => (i.Language, Key: selected.Contains(i.Namespace)
            ? (string.IsNullOrEmpty(prefix) ? i.Namespace.Split('.').Last() : prefix + "." + i.Namespace.Split('.').Last()) : i.Namespace)).ToList();
        if (destinations.Distinct().Count() != destinations.Count) { StatusText = "Move cancelled: a destination key already exists."; return; }
        var before = CaptureStructure();
        var moved = _bulkOperations.MoveNamespace(AllTranslation, keys, prefix);
        if (moved == 0 || before.Zip(AllTranslation).All(p => p.First.Namespace == p.Second.Namespace)) return;
        _undoRedoService.RecordStructure(before, AllTranslation);
        BulkDeselectAll();
        MarkStructureChanged(AllTranslation.ToNamespaces());
        RefreshTree();
        Search(SearchText, true);
        StatusText = $"Moved {moved} item(s) to '{target}'.";
    }

    [RelayCommand]
    private async Task BulkTranslate()
    {
        if (SelectedKeys.Count == 0) return;
        FlushPendingEdits();
        var keys = SelectedKeys.ToHashSet(StringComparer.Ordinal);
        await RunPreTranslateDialog(AllTranslation.Where(t => keys.Contains(t.Namespace)).ToList());
    }

    [RelayCommand]
    private void BulkApprove()
    {
        if (SelectedKeys.Count == 0) return;
        var keys = SelectedKeys.ToHashSet(StringComparer.Ordinal);
        ApproveItems(AllTranslation.Where(t => keys.Contains(t.Namespace) && !string.IsNullOrEmpty(t.Value) && !t.IsApproved).ToList());
    }

    [RelayCommand]
    private async Task BulkCopyToLanguage()
    {
        if (SelectedKeys.Count == 0 || IsAuditMode) return;
        var primary = PrimaryLanguage;
        var targets = OrderedLanguages().Where(l => l != primary).ToList();
        if (targets.Count == 0)
        {
            await _messageService.ShowMessageAsync("There are no other languages to copy to.", "Copy to Language");
            return;
        }

        var target = await _dialogService.ShowPickAsync("Copy to Language", $"Copy the {primary} values of the selected keys into:", targets, targets[0]);
        if (string.IsNullOrEmpty(target)) return;

        FlushPendingEdits();
        var keys = SelectedKeys.ToList();
        var before = CaptureEntries(AllTranslation.Where(i => keys.Contains(i.Namespace) && i.Language == target));
        _bulkOperations.CopyToLanguage(AllTranslation, keys, primary, target);
        foreach (var pair in before.Where(p => p.Value.Value != p.Key.Value))
        {
            pair.Key.IsApproved = false; pair.Key.ApprovedAtUtc = null;
            pair.Key.LastModifiedUtc = DateTime.UtcNow; pair.Key.ChangeType = Toucan.Core.Contracts.Services.ChangeType.DirectEdit;
        }
        var copied = RecordEntryChanges(before);
        if (copied == 0) { StatusText = "No values changed."; return; }
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        NotifyBulkValueChanges(AllTranslation.Where(t => keySet.Contains(t.Namespace) && t.Language == target).ToList());
        foreach (var vm in PagingController.Data.SelectMany(g => g.AllItems)) vm.Refresh();
        UpdateSummaryInfo();
        StatusText = $"Copied {copied} value(s) from {primary} to {target}.";
    }
}
