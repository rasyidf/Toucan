# Known Bugs

## Active

_(No known bugs at this time)_

See `docs/known-bugs.md` for unfinished features (U1–U5).

## Fixed in 0.14.1

- NsTreeItem lazy-load flattening children
- RenameItem substring corruption
- DeleteItem over-deleting siblings
- YAML round-trip data loss (multi-line, escape, leaf+parent keys)
- UndoRedoService stack reversal after 200 edits
- TranslationItemViewModel dirty tracking bypass
- Silent data loss on window close (no unsaved-changes prompt)
- SourceCodeService thread-safety (ConcurrentBag + case-insensitive exclusion)
- Provider mock fallback reporting Succeeded=true
- Provider language code truncation (Split('-')[0])
- Google Translate HTML entity decoding
- Statusbar language selector shows empty menu
- Duplicate FileWatcherService instance causing inconsistent change detection
- CreateNewItem blocking valid keys due to Contains check
- Delete/F2/Escape key binding capturing TextBox keystrokes
- FileWatcherService race condition on _pending flag
- PreTranslateViewModel CancellationTokenSource leak
- ProviderSettingsViewModel losing edits on selection change
- NewProjectViewModel.NextStep UI deadlock risk

## Fixed in 0.14.2

- TranslationDetailsView UpdateSummaryInfo per-keystroke (debounced 300ms)
- JsonSaveStrategy key order instability (sorted by namespace)
- All load strategies lacking directory exclusion (centralized FileEnumerator with flags)
- PlaceholderService.Validate count mismatch (count-aware comparison)
- JsonLoadStrategy migrated to shared FileEnumerator with SkipNestedLocaleDirs
