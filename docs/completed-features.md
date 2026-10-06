# Shipped Features

> As of v0.18.1 (macOS and Linux) and v0.17.3 (Windows). Release notes are in [CHANGELOG.md](../CHANGELOG.md);
> planned work is in [todos/future-roadmap.md](todos/future-roadmap.md).
> Unless a line says otherwise, a feature is in the Windows (WPF) app. The macOS and Linux (Avalonia) app shares the
> core (formats, validation, providers) and has most of the UI; it does not have the Source Control, Translation, and
> Dictionary panels yet.

## Core Editor

- [x] Load/save JSON (flat + namespaced)
- [x] Tree view + List view toggle
- [x] Add / Remove / Rename / Duplicate translation IDs
- [x] Add / Remove languages
- [x] Undo / Redo (Ctrl+Z / Ctrl+Y)
- [x] Comment field per translation
- [x] Approved flag (toggle per row)
- [x] Spell checking (WPF native)
- [x] Word-wrapping for long IDs
- [x] Plain text keys mode (no dot-splitting)
- [x] Auto-select newly added ID
- [x] Pagination with compact controls
- [x] Infinite scroll toggle (paginated vs continuous)
- [x] Cut / Copy / Paste translation values
- [x] Copy as template (1–5 configurable patterns)
- [x] Convert case (lower / upper / sentence / title)
- [x] Remove whitespace (trim / line-by-line / simplify)
- [x] Tab between edit fields
- [x] Filter expression history (last 15)
- [x] Statistics dialog (visual grid, per-language)
- [x] Custom language codes (alias mapping)
- [x] Better font for RTL languages

## Editor Modes

- [x] Focused Editor mode (single-key form, prev/next navigation)
- [x] Zen mode (hides toolbar/statusbar/sidepanel, F11)
- [x] PanelService for centralized panel state
- [x] Focused Editor: language subset selector
- [x] Zen mode: keyboard-only navigation (j/k)

## File I/O

- [x] JSON (flat), JSON (namespaced/i18next)
- [x] YAML, TOML, INI
- [x] PO / Gettext
- [x] RESX (.NET), Android XML, iOS .strings
- [x] XLIFF, ARB (Flutter), CSV
- [x] Import / Export menu (all formats)
- [x] Project manifest (`toucan.tproj`, JSON)
- [x] Excel (.xlsx) import/export
- [x] Export/Import approved flags + comments in Excel
- [x] Java .properties (ISO-8859-1)
- [x] Laravel PHP file support
- [x] Auto-detect framework on folder drop
- [x] File watcher (reload changed files from disk)
- [x] Skip unchanged files on reload

## Machine Translation

- [x] Google Translate, DeepL, Microsoft, OpenAI providers
- [x] Provider settings dialog
- [x] Translation context passed to providers
- [x] Formality setting (formal / informal)
- [x] Remember last translation service
- [x] Preserve parameters (`{{var}}`, `{0}`, `%s`, `:param`)
- [x] Keep uppercase first letter
- [x] Per-language / per-namespace / per-key pre-translate
- [x] Preview before apply
- [x] Suggestions panel (fuzzy-match existing translations)
- [x] Pre-translate plural forms (i18next/ICU)
- [x] Translation memory (reuse across projects)
- [x] Custom webhook provider (your own endpoint, optional auth header)

## UI / UX

- [x] WPF UI Fluent (Mica backdrop, system theme)
- [x] Design system tokens (shared ResourceDictionary)
- [x] Framework tile grid for new projects (16 frameworks)
- [x] Start screen with quick actions
- [x] Recent projects flyout (last 10, clear)
- [x] Auto-open last project on startup
- [x] Reveal in Explorer
- [x] Centralized KeybindingService (22+ shortcuts)
- [x] Keyboard Shortcuts page in Settings (reference only, not editable yet)
- [x] Settings dialog with 12 pages and two-tier settings (app defaults, per-project overrides)
- [x] Filter: untranslated / translated / approved
- [x] Status bar (project, language, cursor, loading, notifications)
- [x] Toucan icon in TitleBar
- [x] Compact pagination controls
- [x] Editor, Review, and Audit modes; Zen mode (J/K navigation)
- [x] Activity bar with side panels: Explorer, Source Code, Search, Issues, Source Control, Languages, Inspector, Translation, Memory (Dictionary panel is a placeholder)
- [x] Panel layout saved between sessions

## Search, Bulk Edits, Validation

- [x] Search and replace across keys, values, and languages (regex, scope, preview, history)
- [x] Bulk delete, move to namespace, pre-translate, approve, and copy source to target
- [x] Six built-in validation rules: missing, placeholder mismatch, duplicate keys, untranslated (same as source), empty, whitespace mismatch
- [x] Per-rule enable/disable and severity
- [x] Custom rules per project: max length, forbidden words, regex, required

## Translation Memory

- [x] Trigram fuzzy matching with configurable threshold and scope (global or project)
- [x] TMX import and export
- [x] View, delete, and clear stored entries
- [ ] Inline ghost-text suggestions (suggestion is computed; the editor overlay is not built yet)

## Advanced Data Model

- [x] Plural forms support (i18next `_one/_other`, ICU)
- [x] Array support in JSON
- [x] Package support (multiple translation sets)
- [x] Drag & drop reorder in tree
- [x] Multi-selection in tree (batch operations)

## Distribution & Platform

- [x] Avalonia app for macOS (`Toucan.app`) and Linux (tarball), preview since v0.18.0
- [x] Windows packaging scripts: portable EXE, Inno Setup installer, MSIX (`publish.ps1`)
- [ ] Signed packages and a release pipeline
- [ ] Auto-updater (the About page has the settings, but nothing checks for updates yet)
- [x] File association (`.tproj` → open app)
- [x] Git branch and changed-file count in the status bar
- [x] Translation file locations configurable per language

## AI Features (Low Priority)

- [ ] ConsistencyAI (check translations against source language)
- [ ] AI-powered translation memory

## Source Code Integration (Low Priority)

- [x] Source code view panel (show where key is used)
- [x] Extract translation IDs from source (`t('key')` patterns)
- [x] Filter: used / unused in source code
- [x] Double-click opens source in external editor
- [x] Wire source root configuration
- [x] Support `.tsx`, `.vue`, `.svelte`, `.py` scanning

## Plugins (v0.18.0; macOS/Linux app and CLI, not the Windows app)

- [x] Plugins add file formats, translation providers, validation rules, and framework profiles from `.dll` assemblies
- [x] Trust model: a plugin loads only when enabled and trusted (SHA-256 of its files)
- [x] `Toucan.Plugins.Abstractions` contract package (plugin API 1.0) and a sample plugin

## Command Line

- [x] `toucan check` (exits 1 on errors, for CI), `stats`, `translate` (with `--dry-run`), `export`, `list-formats`, `list-keys`, `get`, `set`

---

Effort estimates and the order of upcoming work live in [todos/future-roadmap.md](todos/future-roadmap.md).
