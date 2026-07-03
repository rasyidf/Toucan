using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;
using Toucan.Services;

namespace Toucan.ViewModels;

/// <summary>
/// Bulk operations: multi-select mode, selection state, and batch commands.
/// </summary>
internal partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private ObservableCollection<string> selectedKeys = [];

    public int SelectionCount => SelectedKeys.Count;

    partial void OnSelectedKeysChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(SelectionCount));
    }

    // ponytail: BulkOperationService is resolved via DI — injected as optional
    private BulkOperationService? _bulkOperationService;
    internal BulkOperationService? BulkOperationService { get => _bulkOperationService; set => _bulkOperationService = value; }

    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        if (!IsMultiSelectMode)
        {
            SelectedKeys.Clear();
            OnPropertyChanged(nameof(SelectionCount));
        }
    }

    [RelayCommand]
    private void BulkSelectAll()
    {
        // Select all visible keys from the current paging controller data
        var visibleKeys = PagingController.Data.Select(g => g.Namespace).Distinct();
        SelectedKeys.Clear();
        foreach (var key in visibleKeys)
        {
            SelectedKeys.Add(key);
        }
        OnPropertyChanged(nameof(SelectionCount));
    }

    [RelayCommand]
    private void BulkDeselectAll()
    {
        SelectedKeys.Clear();
        OnPropertyChanged(nameof(SelectionCount));
    }

    [RelayCommand]
    private void ToggleKeySelection(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!SelectedKeys.Remove(key))
            SelectedKeys.Add(key);
        OnPropertyChanged(nameof(SelectionCount));
    }

    [RelayCommand]
    private void BulkDelete()
    {
        if (SelectedKeys.Count == 0) return;

        if (!_messageService.ShowConfirmation($"Delete {SelectedKeys.Count} selected key(s)?\nThis action cannot be undone."))
            return;

        var svc = _bulkOperationService ?? new BulkOperationService();
        svc.Delete(AllTranslation, SelectedKeys);

        SelectedKeys.Clear();
        OnPropertyChanged(nameof(SelectionCount));
        RefreshTree();
        Search(SearchText, true);
        IsDirty = true;
        StatusText = "Bulk delete complete.";
    }

    [RelayCommand]
    private void BulkMoveToNamespace()
    {
        if (SelectedKeys.Count == 0) return;

        var target = _dialogService.ShowPrompt("Move to Namespace", "Enter target namespace prefix:", "");
        if (target == null) return;

        var svc = _bulkOperationService ?? new BulkOperationService();
        int moved = svc.MoveNamespace(AllTranslation, SelectedKeys, target);

        SelectedKeys.Clear();
        OnPropertyChanged(nameof(SelectionCount));
        RefreshTree();
        Search(SearchText, true);
        IsDirty = true;
        StatusText = $"Moved {moved} item(s) to '{target}'.";
    }

    [RelayCommand]
    private async Task BulkTranslate()
    {
        if (SelectedKeys.Count == 0) return;

        var selectedItems = AllTranslation.Where(t => SelectedKeys.Contains(t.Namespace)).ToList();
        if (selectedItems.Count == 0) return;

        // Reuse existing pre-translate flow
        if (_pretranslationService != null)
        {
            var languages = AllTranslation.ToLanguages().ToList();
            var vm = _preTranslateFactory != null
                ? _preTranslateFactory(languages, selectedItems, _pretranslationService)
                : new PreTranslateViewModel(languages, selectedItems, _pretranslationService, _dialogService, _providerSettingsService);

            if (_dialogService.ShowPreTranslate(vm))
            {
                NotifyBulkValueChanges(selectedItems);
                Search(SearchText, true);
                StatusText = $"Pre-translated {SelectedKeys.Count} key(s).";
            }
        }
        else if (_bulkActionService != null)
        {
            await _bulkActionService.PreTranslateAsync(selectedItems).ConfigureAwait(true);
            NotifyBulkValueChanges(selectedItems);
            Search(SearchText, true);
            StatusText = $"Pre-translated {SelectedKeys.Count} key(s).";
        }
    }

    [RelayCommand]
    private void BulkApprove()
    {
        if (SelectedKeys.Count == 0) return;

        var items = AllTranslation.Where(t => SelectedKeys.Contains(t.Namespace)).ToList();
        foreach (var item in items)
        {
            item.IsApproved = true;
            item.ApprovedAtUtc = System.DateTime.UtcNow;
        }

        NotifyBulkValueChanges(items);
        Search(SearchText, true);
        StatusText = $"Approved {SelectedKeys.Count} key(s).";
    }

    [RelayCommand]
    private void BulkCopyToLanguage()
    {
        if (SelectedKeys.Count == 0) return;

        var primaryLang = ProjectSettings.LoadFrom(CurrentPath)?.PrimaryLanguage ?? "en-US";
        var languages = AllTranslation.ToLanguages().Where(l => l != primaryLang).ToList();
        if (languages.Count == 0)
        {
            _messageService.ShowMessage("No target languages available.");
            return;
        }

        // Prompt user for target language
        var target = _dialogService.ShowPrompt("Copy to Language", $"Enter target language code (e.g. {string.Join(", ", languages.Take(3))}):", languages.FirstOrDefault() ?? "");
        if (string.IsNullOrEmpty(target)) return;

        var svc = _bulkOperationService ?? new BulkOperationService();
        int copied = svc.CopyToLanguage(AllTranslation, SelectedKeys, primaryLang, target);

        var changedItems = AllTranslation.Where(t => SelectedKeys.Contains(t.Namespace) && t.Language == target).ToList();
        NotifyBulkValueChanges(changedItems);
        Search(SearchText, true);
        StatusText = $"Copied {copied} value(s) from '{primaryLang}' to '{target}'.";
    }
}
