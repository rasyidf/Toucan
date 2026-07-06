# Known Issues & Unfinished Features

Last updated: 2026-07-06 (v0.17.1)

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

- Performance not profiled yet (deferred)
- No auto-updater — planned for v1.1
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
