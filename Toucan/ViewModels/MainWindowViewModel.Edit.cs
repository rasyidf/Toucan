using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Extensions;
using Toucan.Services;

namespace Toucan.ViewModels;

/// <summary>
/// Edit operations: add/rename/delete items, undo/redo, text transforms, clipboard, bulk edits.
/// </summary>
internal partial class MainWindowViewModel
{
    #region Add / Rename / Delete

    [RelayCommand]
    private void NewLanguage()
    {
        var result = _dialogService.ShowLanguagePrompt("New Language", "Enter the translation language name below.", AllTranslation);
        if (result != null)
        {
            AddLanguage(result);
        }
    }

    [RelayCommand]
    private void ManageLanguages()
    {
        var primaryLang = Core.Models.ProjectSettings.LoadFrom(CurrentPath)?.PrimaryLanguage ?? "en-US";
        var result = _dialogService.ShowManageLanguages(AllTranslation, primaryLang);
        if (result == null)
        {
            return;
        }

        // Apply removals
        foreach (var lang in result.RemovedLanguages)
        {
            _ = AllTranslation.RemoveAll(t => t.Language == lang);
        }

        // Apply additions
        foreach (var lang in result.AddedLanguages)
        {
            if (!AllTranslation.Any(t => t.Language == lang))
            {
                AllTranslation.Add(new TranslationItem
                {
                    Namespace = "",
                    Value = "",
                    Language = lang
                });
            }
        }

        if (result.RemovedLanguages.Count > 0 || result.AddedLanguages.Count > 0)
        {
            AddMissingTranslations();
            UpdateSummaryInfo();
            RefreshTree();
            Search("", true);
            IsDirty = true;
            StatusText = $"Languages updated: +{result.AddedLanguages.Count} added, -{result.RemovedLanguages.Count} removed.";
        }
    }

    [RelayCommand]
    private void DeleteLanguage(Core.Models.SummaryItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Language))
        {
            return;
        }

        if (!_messageService.ShowConfirmation($"Delete all translations for '{item.Language}'?\nThis action cannot be undone."))
        {
            return;
        }

        _ = AllTranslation.RemoveAll(t => t.Language == item.Language);
        UpdateSummaryInfo();
        RefreshTree();
        Search("", true);
        IsDirty = true;
        StatusText = $"Language '{item.Language}' deleted.";
    }

    [RelayCommand]
    private void NewItem()
    {
        string ns = SelectedNode?.Namespace ?? "";
        var result = _dialogService.ShowPrompt("New Translation", "Please enter an ID for the translation\nUse '.' to create hierarchical IDs.", ns);
        if (result != null)
        {
            CreateNewItem(result);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void RenameItem()
    {
        var node = SelectedNode;
        if (node == null)
        {
            return;
        }

        var result = _dialogService.ShowPrompt("Rename: " + node.Name, "Enter the new name below.", node.Name);
        if (result != null)
        {
            RenameItem(node, result);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        var node = SelectedNode;
        if (node != null)
        {
            DeleteItem(node);
        }
    }

    private bool CanRenameItem()
    {
        return SelectedNode != null;
    }

    private bool CanDeleteItem()
    {
        return SelectedNode != null;
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void DuplicateItem()
    {
        var node = SelectedNode;
        if (node == null)
        {
            return;
        }

        var newNs = node.Namespace + "_copy";
        List<TranslationItem> existing = AllTranslation.Where(t => t.Namespace == node.Namespace).ToList();
        foreach (var item in existing)
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNs,
                Value = item.Value,
                Language = item.Language,
                Comment = item.Comment,
                IsApproved = false
            });
        }
        RefreshTree(newNs);
        UpdateSummaryInfo();
        Search(newNs, true);
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate1()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var template = AppOptions?.CopyTemplate1 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate2()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var template = AppOptions?.CopyTemplate2 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate3()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var template = AppOptions?.CopyTemplate3 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
    }

    public void AddLanguage(string newLanguage)
    {
        if (string.IsNullOrWhiteSpace(newLanguage))
        {
            return;
        }

        if (AllTranslation.Any(setting => setting.Language == newLanguage))
        {
            _messageService.ShowMessage("Duplicate language");
            return;
        }

        AllTranslation.Add(new TranslationItem
        {
            Namespace = "",
            Value = "",
            Language = newLanguage
        });

        AddMissingTranslations();
        UpdateSummaryInfo();
        RefreshTree();
        Search("", true);
        // Ensure the UI paging is refreshed after adding translations
        Search("", true);
    }

    public void RenameItem(NsTreeItem node, string newName)
    {
        if (node == null || string.IsNullOrWhiteSpace(newName) || newName.Contains('.'))
        {
            return;
        }

        string oldNs = node.Namespace;
        string newNs = oldNs[..oldNs.LastIndexOf(node.Name, StringComparison.InvariantCulture)] + newName.Trim();

        AllTranslation.ForParse().ToList().ForEach(item =>
        {
            if (item.Namespace.StartsWith(oldNs, StringComparison.InvariantCulture))
            {
                item.Namespace = item.Namespace.Replace(oldNs, newNs, StringComparison.InvariantCulture);
            }
        });

        RefreshTree(newNs);
        Search(newNs, true);
    }

    public void DeleteItem(NsTreeItem node)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.Namespace))
        {
            return;
        }

        if (node.Parent == null)
        {
            _ = CurrentTreeItems.Remove(node);
        }
        else if (node.Parent.Items is List<NsTreeItem> siblings)
        {
            _ = siblings.Remove(node);
        }

        _ = AllTranslation.RemoveAll(o => o?.Namespace?.StartsWith(node.Namespace) ?? false);
        RefreshTree();
        Search("", true);
    }

    public void CreateNewItem(string newNamespace)
    {
        if (string.IsNullOrWhiteSpace(newNamespace))
        {
            return;
        }

        if (AllTranslation.NoEmpty().Any(setting => setting.Namespace.Contains(newNamespace)))
        {
            _messageService.ShowMessage("Duplicate name");
            return;
        }

        List<string> languages = AllTranslation.ToLanguages().ToList();
        foreach (string lang in languages)
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNamespace,
                Value = string.Empty,
                Language = lang
            });
        }

        RefreshTree(newNamespace);
        UpdateSummaryInfo();
        Search(newNamespace, true);

        // Auto-select the newly created node
        var newNode = CurrentTreeItems.SelectMany(FindAll).FirstOrDefault(n => n.Namespace == newNamespace);
        if (newNode != null)
        {
            SelectedNode = newNode;
        }
    }

    #endregion

    #region Multi-Select Operations

    [RelayCommand]
    private void ToggleNodeSelection(NsTreeItem node)
    {
        if (node == null)
        {
            return;
        }

        node.IsSelected = !node.IsSelected;
        if (node.IsSelected)
        {
            SelectedNodes.Add(node);
        }
        else
        {
            _ = SelectedNodes.Remove(node);
        }
    }

    [RelayCommand]
    private void DeleteSelectedItems()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        if (!_messageService.ShowConfirmation($"Delete {SelectedNodes.Count} selected items?"))
        {
            return;
        }

        foreach (var node in SelectedNodes.ToList())
        {
            _ = AllTranslation.RemoveAll(o => o.Namespace != null && o.Namespace.StartsWith(node.Namespace));
        }
        SelectedNodes.Clear();
        RefreshTree();
        UpdateSummaryInfo();
        Search("", true);
        IsDirty = true;
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedNodes.Clear();
        foreach (var node in CurrentTreeItems.SelectMany(FindAll))
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var node in SelectedNodes)
        {
            node.IsSelected = false;
        }

        SelectedNodes.Clear();
    }

    private static IEnumerable<NsTreeItem> FindAll(NsTreeItem node)
    {
        yield return node;
        if (node.Items != null)
        {
            foreach (var child in node.Items.SelectMany(FindAll))
            {
                yield return child;
            }
        }
    }

    #endregion

    #region Bulk Edit Operations

    [RelayCommand]
    internal void AddMissingTranslations()
    {
        List<string> namespaces = AllTranslation.ToNamespaces().ToList();
        List<string> allLanguages = AllTranslation.ToLanguages().ToList();

        foreach (string language in allLanguages)
        {
            IEnumerable<string> languageNamespaces = AllTranslation.OnlyLanguage(language).ToNamespaces();
            AllTranslation.AddRange(namespaces.Except(languageNamespaces).Select(o => new TranslationItem() { Namespace = o, Value = string.Empty, Language = language }));
        }
    }

    // Fill empty translations — alias for pre-translate to keep behaviour explicit
    [RelayCommand]
    private async Task FillEmptyTranslations()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to fill.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        await _bulkActionService.PreTranslateAsync(AllTranslation).ConfigureAwait(true);
        UpdateSummaryInfo();
        IsDirty = true;
    }

    [RelayCommand]
    private void DeleteUnusedTranslations()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded.");
            return;
        }

        if (!_messageService.ShowConfirmation("Delete all IDs that have no translation values for any language?"))
        {
            return;
        }

        List<string> emptyNamespaces = AllTranslation.GroupBy(t => t.Namespace)
            .Where(g => g.All(i => string.IsNullOrEmpty(i.Value)))
            .Select(g => g.Key)
            .ToList();

        foreach (var ns in emptyNamespaces)
        {
            _ = AllTranslation.RemoveAll(o => o.Namespace == ns);
        }

        RefreshTree();
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = $"Deleted {emptyNamespaces.Count} unused IDs";
    }

    #endregion

    #region Undo / Redo

    [RelayCommand]
    private void Undo()
    {
        var action = Services.UndoRedoService.Instance.Undo();
        if (action == null)
        {
            return;
        }

        ApplyUndoRedo(action.Namespace, action.Language, action.OldValue);
    }

    [RelayCommand]
    private void Redo()
    {
        var action = Services.UndoRedoService.Instance.Redo();
        if (action == null)
        {
            return;
        }

        ApplyUndoRedo(action.Namespace, action.Language, action.NewValue);
    }

    private void ApplyUndoRedo(string ns, string language, string value)
    {
        var item = AllTranslation?.FirstOrDefault(t => t.Namespace == ns && t.Language == language);
        if (item == null)
        {
            return;
        }

        item.Value = value;
        IsDirty = true;
        // Refresh UI if currently viewing this namespace
        Search(SearchText ?? "", true);
    }

    #endregion

    #region Edit Actions (Convert Case, Trim, Clipboard)

    private void ApplyToAllValues(Func<string, string> transform)
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        foreach (var item in AllTranslation.Where(t => !string.IsNullOrEmpty(t.Value)))
        {
            item.Value = transform(item.Value);
        }

        IsDirty = true;
        Search(SearchText ?? "", true);
    }

    [RelayCommand]
    private void ConvertLowercase()
    {
        ApplyToAllValues(v => v.ToLowerInvariant());
    }

    [RelayCommand]
    private void ConvertUppercase()
    {
        ApplyToAllValues(v => v.ToUpperInvariant());
    }

    [RelayCommand]
    private void ConvertSentenceCase()
    {
        ApplyToAllValues(v => v.Length > 0 ? char.ToUpper(v[0]) + v[1..].ToLowerInvariant() : v);
    }

    [RelayCommand]
    private void ConvertTitleCase()
    {
        ApplyToAllValues(v => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(v.ToLowerInvariant()));
    }

    [RelayCommand]
    private void TrimWhitespace()
    {
        ApplyToAllValues(v => v.Trim());
    }

    [RelayCommand]
    private void TrimLineByLine()
    {
        ApplyToAllValues(v => string.Join("\n", v.Split('\n').Select(l => l.Trim())));
    }

    [RelayCommand]
    private void SimplifyWhitespace()
    {
        ApplyToAllValues(v => System.Text.RegularExpressions.Regex.Replace(v.Trim(), @"\s+", " "));
    }

    [RelayCommand]
    private void EditCut()
    {
        if (SelectedNode == null)
        {
            return;
        }

        List<TranslationItem> items = AllTranslation.Where(t => t.Namespace == SelectedNode.Namespace).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var text = string.Join("\n", items.Select(t => $"{t.Language}={t.Value}"));
        System.Windows.Clipboard.SetText(text);
        DeleteItem(SelectedNode);
    }

    [RelayCommand]
    private void EditCopy()
    {
        if (SelectedNode == null)
        {
            return;
        }

        List<TranslationItem> items = AllTranslation.Where(t => t.Namespace == SelectedNode.Namespace).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var text = string.Join("\n", items.Select(t => $"{t.Language}={t.Value}"));
        System.Windows.Clipboard.SetText(text);
    }

    [RelayCommand]
    private void EditPaste()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var text = System.Windows.Clipboard.GetText();
        // Parse lines in format "language=value" and apply to current selected node
        if (SelectedNode == null)
        {
            return;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var lang = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            var existing = AllTranslation.FirstOrDefault(t => t.Namespace == SelectedNode.Namespace && t.Language == lang);
            _ = existing?.Value = val;
        }
        IsDirty = true;
        Search(SelectedNode.Namespace, true);
    }

    #endregion
}
