using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Edit operations: keys, languages, undo/redo, text transforms, and clipboard.</summary>
public partial class MainWindowViewModel
{
    // ───────────────────────── Languages ─────────────────────────

    [RelayCommand]
    private async Task NewLanguage()
    {
        if (!HasProject) return;
        var code = await _dialogService.ShowLanguagePromptAsync("New Language", "Choose the language to add to this project.", AllTranslation);
        if (code != null) await AddLanguageAsync(code);
    }

    public async Task AddLanguageAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (AllTranslation.Any(t => string.Equals(t.Language, code, StringComparison.OrdinalIgnoreCase)))
        {
            await _messageService.ShowMessageAsync($"'{code}' is already in this project.", "New Language");
            return;
        }

        var result = await _languageManagement.AddLanguageAsync(code);
        if (!result.Success)
        {
            await _messageService.ShowMessageAsync(result.ErrorMessage ?? "Failed to add language.", "New Language");
            return;
        }

        AfterLanguagesChanged($"Added language {code}.");
    }

    [RelayCommand]
    private async Task ManageLanguages()
    {
        if (!HasProject) return;
        var result = await _dialogService.ShowManageLanguagesAsync(AllTranslation, PrimaryLanguage);
        if (result == null) return;

        foreach (var lang in result.RemovedLanguages)
        {
            var r = await _languageManagement.RemoveLanguageAsync(lang);
            if (!r.Success) await _messageService.ShowMessageAsync(r.ErrorMessage ?? $"Failed to remove {lang}.", "Manage Languages");
        }
        foreach (var lang in result.AddedLanguages)
        {
            var r = await _languageManagement.AddLanguageAsync(lang);
            if (!r.Success) await _messageService.ShowMessageAsync(r.ErrorMessage ?? $"Failed to add {lang}.", "Manage Languages");
        }

        var ordered = result.Languages.Select(l => l.Code).ToList();
        await _languageManagement.ReorderLanguagesAsync(ordered);

        if (ProjectSettings != null && !string.Equals(ProjectSettings.PrimaryLanguage, result.PrimaryLanguage, StringComparison.Ordinal))
        {
            ProjectSettings.PrimaryLanguage = result.PrimaryLanguage;
            ProjectSettings.Save();
        }
        PrimaryLanguage = result.PrimaryLanguage;

        AfterLanguagesChanged($"Languages updated: +{result.AddedLanguages.Count}, −{result.RemovedLanguages.Count}.");
    }

    [RelayCommand]
    private async Task DeleteLanguage(SummaryItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Language)) return;
        if (string.Equals(item.Language, PrimaryLanguage, StringComparison.OrdinalIgnoreCase))
        {
            await _messageService.ShowMessageAsync("The primary language can't be deleted. Make another language primary first.", "Delete Language");
            return;
        }
        if (!await _messageService.ConfirmAsync($"Delete all translations for '{item.Language}'? The language files are removed from disk.", "Delete Language", "Delete", "Cancel"))
            return;

        var result = await _languageManagement.RemoveLanguageAsync(item.Language);
        if (!result.Success)
        {
            await _messageService.ShowMessageAsync(result.ErrorMessage ?? "Failed to remove language.", "Delete Language");
            return;
        }
        AfterLanguagesChanged($"Language '{item.Language}' deleted.");
    }

    /// <summary>The language service mutates the store; rebuild the working copy from it.</summary>
    private void AfterLanguagesChanged(string status)
    {
        AllTranslation = _translationStore.Translations.ToList();
        var added = AddMissingTranslationsCore();
        if (added.Count > 0) _translationStore.AddItems(added);
        _hasUntrackedChanges = true;
        IsDirty = true;

        if (StatusBarService.Instance.ViewModel is { } sb)
        {
            sb.AvailableLanguages.Clear();
            foreach (var lang in OrderedLanguages()) sb.AvailableLanguages.Add(lang);
        }
        InitLanguageVisibilityFilter(OrderedLanguages());
        RefreshTree();
        UpdateSummaryInfo();
        Search(SearchText, true);
        StatusText = status;
    }

    /// <summary>Makes <paramref name="language"/> the project's primary (source) language.</summary>
    public void SetPrimaryLanguage(string language)
    {
        if (string.IsNullOrEmpty(language)) return;
        if (ProjectSettings != null)
        {
            ProjectSettings.PrimaryLanguage = language;
            ProjectSettings.Save();
        }
        PrimaryLanguage = language;
        InitLanguageVisibilityFilter(OrderedLanguages());
        UpdateSummaryInfo();
        Search(SearchText, true);
    }

    [RelayCommand]
    private void MakePrimaryLanguage(SummaryItem? item)
    {
        if (item != null) SetPrimaryLanguage(item.Language);
    }

    // ───────────────────────── Keys ─────────────────────────

    [RelayCommand]
    private async Task NewItem()
    {
        if (!HasProject) return;
        var prefix = SelectedNode is { } node ? (node.HasItems ? node.Namespace + "." : ParentPrefix(node.Namespace)) : string.Empty;
        var result = await _dialogService.ShowPromptAsync("New Translation Key",
            "Enter the key. Use '.' to nest keys (e.g. common.buttons.save).", prefix);
        if (result != null) await CreateNewItemAsync(result.Trim());
    }

    private static string ParentPrefix(string ns) => ns.Contains('.', StringComparison.Ordinal) ? ns[..(ns.LastIndexOf('.') + 1)] : string.Empty;

    public async Task CreateNewItemAsync(string newNamespace)
    {
        if (string.IsNullOrWhiteSpace(newNamespace) || newNamespace.EndsWith('.') || newNamespace.StartsWith('.')) return;
        if (AllTranslation.Any(t => t.Namespace == newNamespace))
        {
            await _messageService.ShowMessageAsync($"The key '{newNamespace}' already exists.", "New Key");
            return;
        }
        if (AllTranslation.Any(t => t.Namespace.StartsWith(newNamespace + ".", StringComparison.Ordinal)))
        {
            await _messageService.ShowMessageAsync($"'{newNamespace}' is already used as a group of keys.", "New Key");
            return;
        }

        var languages = ProjectLanguages();
        if (languages.Count == 0) languages.Add(PrimaryLanguage);
        foreach (var lang in languages)
            AllTranslation.Add(new TranslationItem { Namespace = newNamespace, Value = string.Empty, Language = lang, LastModifiedUtc = DateTime.UtcNow });

        MarkStructureChanged([newNamespace]);
        RefreshTree();
        UpdateSummaryInfo();
        RevealKey(newNamespace);
        StatusText = $"Added key {newNamespace}";
    }

    private bool HasSelectedNode() => SelectedNode != null;

    [RelayCommand(CanExecute = nameof(HasSelectedNode))]
    private async Task RenameItem()
    {
        if (SelectedNode is not { } node) return;
        var result = await _dialogService.ShowPromptAsync($"Rename “{node.Name}”", "Enter the new name for this segment (no dots).", node.Name);
        if (result == null) return;

        var error = RenameItemCore(node, result);
        if (error != null) await _messageService.ShowMessageAsync(error, "Rename");
    }

    /// <summary>Renames one key segment and every key below it. Returns an error message or null.</summary>
    public string? RenameItemCore(NsTreeItem node, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName)) return "Name can't be empty.";
        if (newName.Contains('.', StringComparison.Ordinal)) return "Name can't contain '.'.";

        var oldNs = node.Namespace;
        var newNs = ParentPrefix(oldNs) + newName;
        if (newNs == oldNs) return null;
        if (AllTranslation.Any(t => t.Namespace == newNs || t.Namespace.StartsWith(newNs + ".", StringComparison.Ordinal)))
            return $"'{newNs}' already exists.";

        FlushPendingEdits();
        var renamed = new List<string>();
        foreach (var item in AllTranslation)
        {
            if (item.Namespace == oldNs) item.Namespace = newNs;
            else if (item.Namespace.StartsWith(oldNs + ".", StringComparison.Ordinal)) item.Namespace = newNs + item.Namespace[oldNs.Length..];
            else continue;
            renamed.Add(item.Namespace);
        }

        MarkStructureChanged(renamed);
        RefreshTree();
        RevealKey(newNs);
        StatusText = $"Renamed {oldNs} → {newNs}";
        return null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedNode))]
    private async Task DeleteItem()
    {
        if (SelectedNode is not { } node) return;
        var count = AllTranslation.Where(t => MatchesNode(t.Namespace, node.Namespace)).Select(t => t.Namespace).Distinct().Count();
        var what = count == 1 ? $"the key '{node.Namespace}'" : $"'{node.Namespace}' and its {count} keys";
        if (!await _messageService.ConfirmAsync($"Delete {what} in every language?", "Delete", "Delete", "Cancel")) return;
        DeleteNamespaces([node.Namespace]);
    }

    /// <summary>Rename by key path (used by editor card context menus).</summary>
    [RelayCommand]
    private async Task RenameKey(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return;
        var node = new NsTreeItem { Namespace = ns, Name = ns.Split('.').Last() };
        var result = await _dialogService.ShowPromptAsync($"Rename “{node.Name}”", "Enter the new name for this segment (no dots).", node.Name);
        if (result == null) return;
        var error = RenameItemCore(node, result);
        if (error != null) await _messageService.ShowMessageAsync(error, "Rename");
    }

    [RelayCommand]
    private async Task DeleteKey(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return;
        if (!await _messageService.ConfirmAsync($"Delete the key '{ns}' in every language?", "Delete", "Delete", "Cancel")) return;
        DeleteNamespaces([ns]);
    }

    [RelayCommand]
    private void DuplicateKey(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return;
        SelectedNode = new NsTreeItem { Namespace = ns, Name = ns.Split('.').Last() };
        DuplicateItem();
    }

    private static bool MatchesNode(string ns, string nodeNs) => ns == nodeNs || ns.StartsWith(nodeNs + ".", StringComparison.Ordinal);

    /// <summary>Deletes the given keys (and any keys nested under them).</summary>
    public void DeleteNamespaces(IReadOnlyCollection<string> namespaces)
    {
        if (namespaces.Count == 0) return;
        FlushPendingEdits();
        var removed = AllTranslation.Where(t => namespaces.Any(n => MatchesNode(t.Namespace, n))).Select(t => t.Namespace).Distinct().ToList();
        AllTranslation.RemoveAll(t => namespaces.Any(n => MatchesNode(t.Namespace, n)));
        SelectedNode = null;
        SelectedGroup = null;
        MarkStructureChanged(removed);
        RefreshTree();
        UpdateSummaryInfo();
        Search(SearchText.TrimEnd('.').Length > 0 && namespaces.Contains(SearchText.TrimEnd('.')) ? string.Empty : SearchText, true);
        StatusText = $"Deleted {removed.Count} key(s)";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedNode))]
    private void DuplicateItem()
    {
        if (SelectedNode is not { } node) return;
        FlushPendingEdits();

        var newNs = node.Namespace + "_copy";
        var n = 2;
        while (AllTranslation.Any(t => t.Namespace == newNs)) newNs = $"{node.Namespace}_copy{n++}";

        foreach (var item in AllTranslation.Where(t => t.Namespace == node.Namespace).ToList())
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNs,
                Value = item.Value,
                Language = item.Language,
                Comment = item.Comment,
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        MarkStructureChanged([newNs]);
        RefreshTree();
        UpdateSummaryInfo();
        RevealKey(newNs);
    }

    /// <summary>Copies the selected key through a copy template (e.g. <c>t('%1')</c>). Parameter is the template index.</summary>
    [RelayCommand]
    private async Task CopyAsTemplate(object? index)
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns)) return;
        var i = index switch
        {
            int n => n,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => 0
        };
        var templates = ProjectSettings?.CopyTemplates is { Count: > 0 } pt ? pt : AppOptions.CopyTemplates;
        var template = i < templates.Count ? templates[i] : "%1";
        await PlatformService.SetClipboardTextAsync(template.Replace("%1", ns, StringComparison.Ordinal));
        StatusText = "Copied " + template.Replace("%1", ns, StringComparison.Ordinal);
    }

    public IReadOnlyList<string> CopyTemplates => ProjectSettings?.CopyTemplates is { Count: > 0 } pt ? pt : AppOptions.CopyTemplates;

    // ───────────────────────── Missing translations ─────────────────────────

    /// <summary>Adds empty items so every key exists in every language. Returns the items added.</summary>
    private List<TranslationItem> AddMissingTranslationsCore()
    {
        var namespaces = AllTranslation.ToNamespaces().ToList();
        var added = new List<TranslationItem>();
        var existing = AllTranslation.Select(t => (t.Language, t.Namespace)).ToHashSet();
        foreach (var language in ProjectLanguages())
        {
            foreach (var ns in namespaces)
            {
                if (existing.Contains((language, ns))) continue;
                var item = new TranslationItem { Namespace = ns, Value = string.Empty, Language = language };
                AllTranslation.Add(item);
                added.Add(item);
            }
        }
        return added;
    }

    [RelayCommand]
    private void AddMissingTranslations()
    {
        var added = AddMissingTranslationsCore();
        if (added.Count == 0)
        {
            StatusText = "Every key already exists in every language.";
            return;
        }
        MarkStructureChanged(added.Select(a => a.Namespace));
        UpdateSummaryInfo();
        Search(SearchText, true);
        StatusText = $"Added {added.Count} missing entries.";
    }

    [RelayCommand]
    private async Task DeleteUnusedTranslations()
    {
        var empty = AllTranslation.ForParse().GroupBy(t => t.Namespace)
            .Where(g => g.All(i => string.IsNullOrEmpty(i.Value)))
            .Select(g => g.Key)
            .ToList();
        if (empty.Count == 0)
        {
            await _messageService.ShowMessageAsync("There are no keys without any value.", "Delete Empty Keys");
            return;
        }
        if (!await _messageService.ConfirmAsync($"Delete {empty.Count} key(s) that have no value in any language?", "Delete Empty Keys", "Delete", "Cancel")) return;
        DeleteNamespaces(empty);
    }

    // ───────────────────────── Undo / redo ─────────────────────────

    [RelayCommand]
    private void Undo()
    {
        FlushPendingEdits();
        // A step may hold several edits (a bulk operation); undo them last to first.
        if (_undoRedoService.Undo() is { } step) ApplyUndoRedo(step.Reverse().Select(a => (a.Namespace, a.Language, a.OldValue)).ToList());
    }

    [RelayCommand]
    private void Redo()
    {
        FlushPendingEdits();
        if (_undoRedoService.Redo() is { } step) ApplyUndoRedo(step.Select(a => (a.Namespace, a.Language, a.NewValue)).ToList());
    }

    private void ApplyUndoRedo(IReadOnlyList<(string Namespace, string Language, string Value)> edits)
    {
        var restored = new List<TranslationItem>();
        foreach (var (ns, language, value) in edits)
        {
            var item = AllTranslation.FirstOrDefault(t => t.Namespace == ns && t.Language == language);
            if (item == null) continue;
            _translationStore.NotifyValueChanged(item, value);
            SessionDirtyKeys.Add(ns);
            restored.Add(item);
        }
        if (restored.Count == 0) return;
        var last = restored[^1];

        SessionDirtyCount = SessionDirtyKeys.Count;
        IsDirty = true;
        BumpWorkspaceRevision();

        var changed = restored.ToHashSet();
        var shown = PagingController.PageData.SelectMany(g => g.AllItems).ToList();
        foreach (var vm in shown.Where(v => changed.Contains(v.Model))) vm.Refresh();
        if (!shown.Any(v => ReferenceEquals(v.Model, last))) RevealKey(last.Namespace);
        UpdateSummaryInfo();
        StatusText = restored.Count == 1 ? $"{last.Namespace} [{last.Language}] restored" : $"{restored.Count} edits restored";
    }

    // ───────────────────────── Text transforms ─────────────────────────

    /// <summary>Applies a transform to the values currently shown in the editor (the active filter).</summary>
    private async Task ApplyToVisibleValues(string label, Func<string, string> transform)
    {
        FlushPendingEdits();
        var planned = PagingController.Data.SelectMany(g => g.AllItems).Select(i => i.Model)
            .Where(t => !string.IsNullOrEmpty(t.Value))
            .Select(t => (Item: t, Next: transform(t.Value)))
            .Where(p => p.Next != p.Item.Value)
            .ToList();

        if (planned.Count == 0)
        {
            StatusText = $"{label}: nothing to change.";
            return;
        }
        if (planned.Count > 50 && !await _messageService.ConfirmAsync($"{label} will change {planned.Count} values in the current view. Continue?", label))
            return;

        foreach (var (item, next) in planned)
        {
            _undoRedoService.Record(item.Namespace, item.Language, item.Value, next);
            item.Value = next;
        }
        var changed = planned.Select(p => p.Item).ToList();

        NotifyBulkValueChanges(changed);
        foreach (var vm in PagingController.PageData.SelectMany(g => g.AllItems)) vm.Refresh();
        StatusText = $"{label}: {changed.Count} value(s) changed.";
    }

    [RelayCommand] private Task ConvertLowercase() => ApplyToVisibleValues("Lowercase", v => v.ToLower(CultureInfo.CurrentCulture));
    [RelayCommand] private Task ConvertUppercase() => ApplyToVisibleValues("Uppercase", v => v.ToUpper(CultureInfo.CurrentCulture));
    [RelayCommand] private Task ConvertSentenceCase() => ApplyToVisibleValues("Sentence case", v => v.Length > 0 ? char.ToUpper(v[0], CultureInfo.CurrentCulture) + v[1..].ToLower(CultureInfo.CurrentCulture) : v);
    [RelayCommand] private Task ConvertTitleCase() => ApplyToVisibleValues("Title case", v => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(v.ToLower(CultureInfo.CurrentCulture)));
    [RelayCommand] private Task TrimWhitespace() => ApplyToVisibleValues("Trim", v => v.Trim());
    [RelayCommand] private Task TrimLineByLine() => ApplyToVisibleValues("Trim lines", v => string.Join('\n', v.Split('\n').Select(l => l.Trim())));
    [RelayCommand] private Task SimplifyWhitespace() => ApplyToVisibleValues("Simplify whitespace", v => WhitespaceRegex().Replace(v.Trim(), " "));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // ───────────────────────── Clipboard ─────────────────────────

    [RelayCommand]
    private async Task EditCopy()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns)) return;
        var items = AllTranslation.Where(t => t.Namespace == ns).OrderBy(t => t.Language, StringComparer.Ordinal).ToList();
        if (items.Count == 0) return;
        await PlatformService.SetClipboardTextAsync(string.Join('\n', items.Select(t => $"{t.Language}={t.Value}")));
        StatusText = $"Copied {items.Count} value(s) of {ns}";
    }

    /// <summary>Copies the selected key's values, then deletes the key (asking first, like Delete does).</summary>
    [RelayCommand]
    private async Task EditCut()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns) || IsAuditMode) return;
        await EditCopy();
        await DeleteKey(ns);
    }

    /// <summary>Pastes "language=value" lines into the selected key.</summary>
    [RelayCommand]
    private async Task EditPaste()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns) || IsAuditMode) return;
        var text = await PlatformService.GetClipboardTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        FlushPendingEdits();
        var changed = new List<TranslationItem>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            var lang = line[..eq].Trim();
            var val = line[(eq + 1)..].TrimEnd('\r');
            var existing = AllTranslation.FirstOrDefault(t => t.Namespace == ns && t.Language == lang);
            if (existing == null || existing.Value == val) continue;
            _undoRedoService.Record(ns, lang, existing.Value, val);
            existing.Value = val;
            changed.Add(existing);
        }
        if (changed.Count == 0) return;
        NotifyBulkValueChanges(changed);
        foreach (var vm in PagingController.PageData.SelectMany(g => g.AllItems)) vm.Refresh();
        UpdateSummaryInfo();
        StatusText = $"Pasted {changed.Count} value(s) into {ns}";
    }

    [RelayCommand]
    private async Task CopyKey(string? ns)
    {
        ns ??= SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (!string.IsNullOrEmpty(ns)) await PlatformService.SetClipboardTextAsync(ns);
    }

    // ───────────────────────── Hidden namespaces ─────────────────────────

    private bool IsNamespaceHidden(string? ns) =>
        !string.IsNullOrEmpty(ns) && HiddenNamespaces.Any(h => ns == h || ns.StartsWith(h + ".", StringComparison.Ordinal));

    [RelayCommand]
    private void HideNamespace(string? ns)
    {
        ns ??= SelectedNode?.Namespace;
        if (string.IsNullOrWhiteSpace(ns) || HiddenNamespaces.Contains(ns)) return;
        HiddenNamespaces.Add(ns);
        PersistHiddenNamespaces();
    }

    [RelayCommand]
    private void UnhideNamespace(string? ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return;
        HiddenNamespaces.Remove(ns);
        PersistHiddenNamespaces();
    }

    private void PersistHiddenNamespaces()
    {
        if (ProjectSettings != null)
        {
            ProjectSettings.HiddenNamespaces = [.. HiddenNamespaces];
            ProjectSettings.Save();
        }
        RefreshTree();
        Search(SearchText, true);
    }
}
