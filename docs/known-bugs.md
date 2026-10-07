# Known Issues & Unfinished Features

Last updated: 2026-10-06. Current release: v0.19.0 for Windows, macOS and Linux (preview). The WPF app was removed; v0.17.3 was its last release (see `legacy/wpf`).

## Fixed in v0.17.2

| # | Severity | Area | Description | File |
|---|----------|------|-------------|------|
| B7 | Medium | WPF UI | Issues panel showed flat list with no grouping — hard to scan large validation results | `Toucan/Views/Panels/IssuesPanel.xaml` |
| B8 | Low | WPF UI | Search panel buttons misaligned with TextBox and ComboBox heights | `Toucan/Views/Panels/SearchPanel.xaml` |
| B9 | Low | WPF UI | Source Code panel: top padding on filter, "Open Settings" opened app preferences instead of project settings, empty state didn't differentiate pre/post-scan | `Toucan/Views/Panels/SourceCodePanel.xaml` |
| B10 | Low | WPF UI | Explorer list view items had hardcoded gray foreground instead of using theme brushes | `Toucan/Views/Components/ResourcesView.xaml` |
| B11 | Medium | WPF UI | Status bar Mode/Stats/VCS panel clicks were no-ops (stub commands) — now cycle mode, run validation, and focus source-control panel | `Toucan/ViewModels/StatusBarPanels/BuiltInPanels.cs`, `Toucan/Views/MainWindow.xaml.cs` |

## Nice-to-Have (Feature Requests)

- **Notification flyout + NotificationService** — Status bar notification badge should open a floating panel/hover card showing notification history. Needs: `NotificationService` singleton (queue with title/message/severity/timestamp), a Popup/Flyout anchored to the badge, and notification sources (update checker, auto-save failures, validation summaries). Scope: new service + new view + wiring into existing panels.

## Fixed in v0.17.1

| # | Severity | Area | Description | File |
|---|----------|------|-------------|------|
| B1 | High | Core Services | DiffMergeEngine.ApplyModification mutated item.Value directly without updating baselines — merged items appeared perpetually dirty | `Toucan.Core/Services/DiffMergeEngine.cs` |
| B2 | Medium | Core Services | AutoSaveService threw ObjectDisposedException if Dispose() called during in-flight save; `_disposed` not volatile | `Toucan.Core/Services/AutoSaveService.cs` |
| B3 | Medium | Core Services | TranslationManagementService TOCTOU race in RaiseDirtyStateChangedIfNeeded — two threads could both see a dirty-state transition and double-fire the event | `Toucan.Core/Services/TranslationManagementService.cs` |
| B4 | High | WPF UI | ProjectLifecycleService auto-reload path ran on thread pool without marshaling to UI thread — UI-bound collections updated from background thread | `Toucan.Core/Services/ProjectLifecycleService.ExternalChanges.cs`, `Toucan/App.xaml.cs` |
| B5 | High | Strategies | IosStringsLoadStrategy Unescape used chained string.Replace — `\\n` in .strings files was corrupted to backslash+newline instead of literal `\n` | `Toucan.Core/Services/LoadStrategies/IosStringsLoadStrategy.cs` |
| B6 | High | Strategies | JavaPropertiesLoadStrategy ignored line continuations (trailing backslash) — multi-line values truncated to first line (data loss) | `Toucan.Core/Services/LoadStrategies/JavaPropertiesLoadStrategy.cs` |

## Open Issues (Not Bugs — Limitations)

- The Avalonia app does not have every panel the retired WPF app had yet: Source Control, Translation, and Dictionary are missing.
- The WPF app was removed from `main`; its source is on the `legacy/wpf` branch and no longer builds against the current Core. Windows users should use the Avalonia app, v0.19.0.
- The macOS app is ad-hoc signed, not notarized: macOS asks you to confirm before the first launch.
- No signed packages and no release pipeline yet (FG-01).

- Performance not profiled yet (deferred)
- No auto-updater: the About page shows the settings, but nothing checks for updates yet. Planned for v0.24 (FG-02 in [todos/future-roadmap.md](todos/future-roadmap.md)).
- Keybinding customization — display-only reference for now
- XLIFF save writes key in `<source>` instead of source-language text (round-trip loses source value)
- PO load/save: no plural form support (msgid_plural/msgstr[N] silently dropped)
- ARB save discards @key metadata (description, placeholders) on round-trip
- YAML save: keys containing literal dots become nested (no way to distinguish from explicit nesting)
- CSV save: `\r` alone in a value doesn't trigger quoting (malformed line break)
- RESX DetectLanguage heuristic can mis-detect filenames like "Resources.Designer" as language "Designer"

## Previously Fixed (v0.14.1–v0.16.1)

All 22 bugs from v0.14.1–v0.14.2 resolved. See CHANGELOG.md for details.
All 5 unfinished features from v0.15.0 resolved in that release.
Manifest/backfill bugs fixed in v0.16.1.
