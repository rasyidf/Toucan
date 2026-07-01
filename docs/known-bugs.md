# Known Bugs & Issues

Last updated: 2026-07-01 (v0.14.2)

## Critical (Data Loss / Corruption Risk)

### 1. NsTreeItem.Items getter flattens children
**File:** `Toucan.Core\Models\NsTreeItem.cs` (Lines 19-30)  
**Status:** Fixed  
The lazy-load Items getter calls `_storage.AddRange(child.Items)` which flattens grandchildren into the current node — children lose their subtree structure and become siblings. Should be `_storage.Add(child)`.

### 2. RenameItem uses substring Replace
**File:** `Toucan\ViewModels\MainWindowViewModel.Edit.cs` (Line ~293)  
**Status:** Fixed  
`item.Namespace.Replace(oldNs, newNs)` renames unrelated keys that contain the old namespace as a substring. E.g., renaming "app" to "app2" also corrupts "wrapper.application" → "wrapper.app2lication". Fix: use exact prefix match with dot delimiter.

### 3. DeleteItem uses StartsWith without delimiter guard
**File:** `Toucan\ViewModels\MainWindowViewModel.Edit.cs` (Line ~313)  
**Status:** Fixed  
`AllTranslation.RemoveAll(o => o.Namespace.StartsWith(node.Namespace))` — deleting "app" also deletes "application", "appSettings", etc. Fix: check `== namespace || StartsWith(namespace + ".")`.

### 4. YAML round-trip data loss
**File:** `Toucan.Core\Services\SaveStrategies\YamlSaveStrategy.cs`, `LoadStrategies\YamlLoadStrategy.cs`  
**Status:** Fixed  
- Save: `\n` in values not escaped to `\\n` inside double-quoted strings — raw newline breaks line-based loader.
- Save: When a key `foo` exists AND `foo.bar` exists, the leaf value for `foo` is lost (empty `GetRemainingKey`).
- Load: Multi-line YAML values (`|`, `>`) not supported — lines without `:` silently skipped.

### 5. UndoRedoService stack reversal on cap
**File:** `Toucan\Services\UndoRedoService.cs` (Lines 27-34)  
**Status:** Fixed  
`Stack<T>.ToArray()` returns newest-first (LIFO). Pushing them back in that order reverses the stack. After 200 edits, undo pops the oldest action instead of the newest. Fix: iterate in reverse when repopulating.

### 6. TranslationItemViewModel dirty tracking bypass
**File:** `Toucan\ViewModels\TranslationItemViewModel.cs` (SaveTranslation method)  
**Status:** Fixed  
`SaveTranslation()` writes to `_model.Value` and records to undo service, but never calls `ITranslationManagementService.NotifyValueChanged()`. The project can appear "saved" when edits exist.

### 7. Missing unsaved-changes guard on window close
**File:** `Toucan\Views\MainWindow.xaml.cs` (Window_Closing)  
**Status:** Fixed  
`Window_Closing` never checks `HasUnsavedChanges`. Also, `IUnsavedChangesHandler` is declared in DI but never wired. Edits silently lost on close.

### 8. SourceCodeService thread-safety
**File:** `Toucan.Core\Services\SourceCodeService.cs` (Lines 43-44)  
**Status:** Fixed  
- `List<KeyUsage>.Add()` inside `ConcurrentDictionary.GetOrAdd()` is not thread-safe under `Parallel.ForEach` — data corruption. Fix: use `ConcurrentBag<KeyUsage>`.
- `s_excludeDirs.Contains(name)` is case-sensitive on Windows — "Node_Modules" or "BIN" won't be excluded. Fix: `StringComparer.OrdinalIgnoreCase`.

## High Priority (Functional Bugs)

### 9. All providers mock fallback reports Succeeded=true
**Files:** `OpenAI/DeepL/Google/MicrosoftTranslationProvider.cs`  
**Status:** Fixed  
When no API key is configured, fallback produces fake `[provider/lang] text` translations marked `Succeeded = true`. Downstream can't distinguish real from mock. Fix: `Succeeded = false`, add `ErrorMessage = "No API key configured"`.

### 10. Provider language code truncation
**Files:** `DeepL/Google/MicrosoftTranslationProvider.cs`  
**Status:** Fixed  
`Split('-')[0]` loses regional variants — breaks `zh-CN`/`zh-TW`, `pt-BR`/`pt-PT`, `EN-US`/`EN-GB`. Fix: pass the full BCP-47 code; only truncate for providers that don't support regional variants.

### 11. GoogleTranslationProvider HTML entity decoding
**File:** `Toucan.Core\Services\Providers\GoogleTranslationProvider.cs`  
**Status:** Fixed  
Google Translate API returns HTML-encoded entities (`&#39;`, `&amp;`). Response used raw without decoding — garbled translations saved. Fix: `WebUtility.HtmlDecode()`.

## Medium Priority (UX / Reliability)

### 12. Duplicate FileWatcherService instance
**File:** `Toucan\Views\MainWindow.xaml.cs` (Line 16)  
**Status:** Fixed  
`new FileWatcherService()` creates a second instance besides the DI singleton. External-change detection is inconsistent.

### 13. FileWatcherService race condition on _pending
**File:** `Toucan\Services\FileWatcherService.cs`  
**Status:** Fixed  
`_pending` flag read/written from multiple threads without synchronization. Fix: use `Interlocked.CompareExchange`.

### 14. Delete key binding captures TextBox keystrokes
**File:** `Toucan\Services\KeybindingService.cs` (Line 70)  
**Status:** Fixed  
`Key.Delete` bound at window level may intercept Delete in TextBoxes causing accidental item deletion.

### 15. CreateNewItem uses Contains for duplicate check
**File:** `Toucan\ViewModels\MainWindowViewModel.Edit.cs` (Line ~335)  
**Status:** Fixed  
`setting.Namespace.Contains(newNamespace)` blocks creating "app" if "wrapper.app.title" exists. Should use exact match.

### 16. PreTranslateViewModel CancellationTokenSource leak
**File:** `Toucan\ViewModels\PreTranslateViewModel.cs`  
**Status:** Fixed  
`_cts` reassigned without disposing previous instance. Fix: dispose before creating new.

### 17. ProviderSettingsViewModel doesn't flush edits on selection change
**File:** `Toucan\ViewModels\ProviderSettingsViewModel.cs`  
**Status:** Fixed  
Switching providers loses unsaved field edits. Fix: flush to previous selection in `RebuildFieldItems`.

### 18. NewProjectViewModel.NextStep deadlock risk
**File:** `Toucan\ViewModels\NewProjectViewModel.cs`  
**Status:** Fixed  
`GetAwaiter().GetResult()` on async dialog — potential UI thread deadlock. Fix: made method `async Task`.

## Low Priority (Performance / Improvements)

### 19. TranslationDetailsView fires UpdateSummaryInfo on every keystroke
**File:** `Toucan\Views\Components\TranslationDetailsView.xaml.cs`  
**Status:** Fixed  
Debounced with a 300ms `DispatcherTimer` — summary only recalculates after idle.

### 20. JsonSaveStrategy doesn't preserve key order
**File:** `Toucan.Core\Services\SaveStrategies\JsonSaveStrategy.cs`  
**Status:** Fixed  
Items sorted by namespace (ordinal) before insertion so JSON keys are stable across saves.

### 21. Load strategies (CSV, YAML, PHP) lack directory exclusion
**Status:** Fixed  
Extracted centralized `FileEnumerator` utility (`Toucan.Core\Services\FileEnumerator.cs`) with `EnumerateOptions` flags. All load strategies (including JSON, which was migrated from its own implementation) now use it. `SkipNestedLocaleDirs` flag auto-detects when root has language-code subdirs and skips nested `locales/`, `i18n/`, `translations/`, `lang/` directories to prevent duplicates.

### 22. PlaceholderService.Validate uses set-based Except
**File:** `Toucan.Core\Services\PlaceholderService.cs`  
**Status:** Fixed  
Replaced with count-aware comparison via `GroupBy` + count diff. Now detects `{0}` appearing twice in source but once in target.

## Unfinished Features

### U1. OptionsViewModel stubs
`BrowseSourceRoot`, `BrowseSourceEditor`, `ConfigureLanguageCodes` — empty bodies with `// future` comments.

### U2. IUnsavedChangesHandler / IExternalChangeHandler never wired
Declared "set later by WPF layer" but no code sets them on the lifecycle service.

### U3. FilterUsedKeys / FilterUnusedKeys don't filter
Sets a flag string but `Search` method doesn't read it — filter never applied.

### U4. ShowMachineTranslations is visual-only toggle
Marked `ponytail:` placeholder — doesn't actually filter/highlight.

### U5. Keybinding shortcuts page is display-only
No editing capability in Options dialog.
