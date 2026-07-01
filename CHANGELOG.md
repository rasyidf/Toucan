# Changelog

## [0.15.0] - 2026-07-01

### Added
- **Modular StatusBar architecture** — Status bar is now composed of independent panels (`IStatusBarPanel`) managed by a `StatusBarPanelRegistry`. Each panel can be shown/hidden, reordered, and clicked.
- **10 built-in panels**: VCS (branch + changes + sync), Translation Stats (progress + per-language breakdown), Mode (EDITOR/REVIEW/AUDIT badge), Project (name + dirty count), Status (ephemeral text), Language (primary + switcher), Encoding (UTF-8), Line Endings (LF/CRLF toggle), Notifications (badge), Loading (spinner).
- **Dynamic panel rendering** — StatusBarView uses `ItemsControl` with implicit `DataTemplate` per panel type. Left/Center/Right alignment groups rendered independently.
- **Panel click actions** — Each panel has a `ClickCommand`: VCS opens summary, Stats opens statistics dialog, Language opens switcher, Line Endings toggles LF↔CRLF, etc.
- **Panel registry API** — `StatusBarPanelRegistry.Register()`, `.Unregister()`, `.SetVisibility()`, `.Reorder()` — external services can add custom panels at runtime.
- **Rich tooltips** — VCS shows branch + change summary, Stats shows per-language progress bars, all panels have contextual tooltips.

### Changed
- **StatusBarViewModel** — Collapsed from 4 partial files into a single file backed by panel instances. Backward-compatible: existing callers (`StatusBarService.UpdateStatus()`, `.UpdateDefaultLanguage()`, etc.) still work unchanged.
- **StatusBarView.xaml** — Replaced hardcoded 8-column Grid with 3-column layout (Left/Center/Right) using `ItemsControl` bound to registry collections.

## [0.14.2] - 2026-07-01

### Improved
- **Centralized FileEnumerator** — Extracted `Toucan.Core\Services\FileEnumerator.cs` with flags-based `EnumerateOptions`. All load strategies (JSON, CSV, YAML, PHP) now use a single file crawler with shared directory exclusion list. `SkipNestedLocaleDirs` flag auto-detects when root has language-code subdirs and skips nested `locales/`, `i18n/`, `translations/`, `lang/` directories to prevent duplicates.
- **UpdateSummaryInfo per-keystroke** — `TranslationDetailsView` now debounces the update event (300ms idle) instead of firing on every KeyUp.
- **JSON key order instability** — `JsonSaveStrategy` now sorts items by namespace before writing, producing stable key order across saves (reduces VCS noise).
- **Placeholder count validation** — `PlaceholderService.Validate` now uses count-aware comparison instead of set-based `Except`. Detects when a placeholder appears more times in source than target.

## [0.14.1] - 2026-07-01

### Fixed
- **Tree corruption on rename** — `RenameItem` used `string.Replace` which corrupted unrelated keys sharing a substring (e.g., renaming "app" mangled "application"). Now uses exact prefix + dot delimiter matching.
- **Tree corruption on delete** — `DeleteItem` used bare `StartsWith` which deleted sibling keys (e.g., deleting "app" also deleted "appSettings"). Now requires exact match or dot-separated child.
- **NsTreeItem lazy-load flattening** — The `Items` getter was flattening grandchildren into the current node via `AddRange(child.Items)`. Children now retain their subtree structure.
- **YAML round-trip data loss** — Save strategy now properly escapes `\n`/`\r`/`\t` in double-quoted values, quotes YAML reserved words (`true`, `false`, `yes`, `no`, `null`), and preserves keys that are both parents and leaf values. Load strategy now supports multi-line block scalars (`|` and `>`).
- **Undo history corruption** — After 200 edits, the undo stack cap logic reversed item order (newest ended up at bottom). Now iterates in reverse when repopulating.
- **Dirty tracking bypass** — `TranslationItemViewModel.SaveTranslation()` now calls `ITranslationManagementService.NotifyValueChanged()` so the project correctly shows unsaved state.
- **Silent data loss on close** — `Window_Closing` now prompts Save/Discard/Cancel when unsaved changes exist, instead of silently discarding edits.
- **Source code scan thread-safety** — Replaced `List<KeyUsage>` with `ConcurrentBag<KeyUsage>` in parallel scan. Fixed case-sensitive directory exclusion on Windows (now uses `StringComparer.OrdinalIgnoreCase`).
- **Provider mock fallback** — All providers (OpenAI, Google, DeepL, Microsoft) now report `Succeeded = false` with "No API key configured" instead of silently injecting fake `[provider/lang]` translations.
- **Provider language code truncation** — Removed `Split('-')[0]` that stripped regional variants. Full BCP-47 codes (e.g., `zh-CN`, `pt-BR`, `EN-US`) are now passed to translation APIs.
- **Google Translate HTML entities** — Response text is now decoded via `WebUtility.HtmlDecode()` to fix garbled apostrophes and ampersands.
- **Statusbar language selector empty** — `AvailableLanguages` collection is now populated from project languages after load, so the inline language switcher works.
- **Duplicate FileWatcherService** — MainWindow now uses the DI-registered singleton `IFileWatcherService` instead of creating its own instance. External-change detection is consistent with the lifecycle service.
- **CreateNewItem false duplicate** — Replaced `Namespace.Contains(newNamespace)` with exact `==` match. Creating "app" is no longer blocked by "wrapper.app.title".
- **Delete/F2/Escape in TextBox** — `HandleZenKeys` now guards these keys when a TextBox has focus, preventing accidental item deletion or rename while typing.
- **FileWatcherService race condition** — Replaced `bool _pending` with `Interlocked.CompareExchange` to prevent missed/double-fired change events from concurrent threads.
- **PreTranslateViewModel CTS leak** — Previous `CancellationTokenSource` is now disposed before creating a new one on each Start().
- **Provider settings lost on switch** — `RebuildFieldItems` now flushes field edits to the *previous* selection before clearing, so switching providers no longer discards unsaved config.
- **NewProjectViewModel deadlock** — Converted `NextStep()` from sync (`GetAwaiter().GetResult()`) to `async Task`, eliminating potential UI thread deadlock on the existing-project dialog.

## [0.14.0] - 2026-06-30

### Added
- **Provider Settings integration** — Providers (Google, DeepL, Microsoft, OpenAI, Custom Webhook) now have proper schema definitions with default values. API keys are stored encrypted via DPAPI. Pre-translation works out of the box once an API key is saved.
- **Project Properties dialog** — Standalone dialog (File → Project Properties, or toolbar button) with 5 pages: Identity, Translation, Editor, Features, Source Code. Separated from the global Options dialog.
- **Namespace hiding** — Right-click any namespace in the tree to hide it from the editor and statistics. Manage hidden namespaces in Project Properties → Editor. Persisted in `toucan.project`.
- **Session dirty tracking** — Modified keys show a caution-colored left indicator bar. Statusbar displays a dirty count badge. Cleared on save.
- **BreadcrumbBar** — Added WPF-UI BreadcrumbBar to the Resources panel showing the selected namespace path.
- **Fluent MessageBox** — All user-facing message dialogs now use `Wpf.Ui.Controls.MessageBox` instead of native Win32 MessageBox.
- **Default project folder** — New Project dialog defaults to `Documents/Toucan/{project-name}`, auto-updating as you type.
- **Existing project detection** — New Project wizard detects existing `toucan.project` and offers to open or overwrite.
- **Statusbar language selector** — Click the language code in the statusbar to switch the active language via popup menu.
- **Provider management tests** — 11 new tests covering provider settings (defaults, add/remove, save flush, schema fields).

### Changed
- **Pre-Translate dialog** — Redesigned with dual-panel layout (config left, preview right), dropdown provider selector, compact options, Fluent-styled table (no more Vista GridView).
- **Inspector panel** — Tabs now use custom Fluent-styled underline indicator (accent-colored bottom border on selected tab). Removed native TabControl chrome.
- **About page** — Redesigned to match Files App style: app card with Copy button, Help & support links, Open source section.
- **New Project dialog** — Framework tiles reduced (80×60), project name/folder moved to top, Step 2 made compact with inline add buttons.
- **Suggested Languages** — Redesigned with card-style items (Globe icon, Fluent Add button).
- **Mode selector** — Made more compact (26px height, 11px font, reduced padding).
- **Zen mode** — Card now has `MinWidth="500"`, `MaxWidth="900"`, proper card styling with rounded corners.
- **Focused Editor mode** — Now functional: shows single item with navigation bar (↑/↓/exit) when activated.
- **Pagination buttons** — Disabled state uses opacity instead of opaque background (dark mode fix).
- **Language Prompt dialog** — Width set to 400px (was stretching full screen).

### Fixed
- **Pretranslation mock output** (`[provider/lang]`) — Root cause: `IProviderSettingsService` was not being passed to `PreTranslateViewModel`. Empty API keys were being encrypted/stored. Fixed both the DI wiring and the save/load logic to skip empty secrets.
- **Duplicate translations** — When root folder has both `en/` dirs and a nested `locales/en/` dir, the loader now skips the nested `locales/` to prevent duplicates.
- **"Showing N of M items"** — Removed redundant text from statusbar (pagination already shows this).
- **Provider settings button cropped** — Replaced narrow `Width="30"` button with proper `ui:Button` + SymbolIcon.
- **Project Languages showing only 1** — Now loads discovered languages from actual files, not just manifest.
