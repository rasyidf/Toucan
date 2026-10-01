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
        if (SelectedKeys.Count == 0) return;
        var target = await _dialogService.ShowPromptAsync("Move to Namespace", "Enter the target key prefix (e.g. common.buttons). Leave empty to move to the root.");
        if (target == null) return;

        FlushPendingEdits();
        var keys = SelectedKeys.ToList();
        var moved = _bulkOperations.MoveNamespace(AllTranslation, keys, target.Trim().TrimEnd('.'));
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
        if (SelectedKeys.Count == 0) return;
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
        var copied = _bulkOperations.CopyToLanguage(AllTranslation, keys, primary, target);
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        NotifyBulkValueChanges(AllTranslation.Where(t => keySet.Contains(t.Namespace) && t.Language == target).ToList());
        foreach (var vm in PagingController.Data.SelectMany(g => g.AllItems)) vm.Refresh();
        UpdateSummaryInfo();
        StatusText = $"Copied {copied} value(s) from {primary} to {target}.";
    }
}
