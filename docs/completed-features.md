---
title: "Shipped Features"
status: active
updated: 2026-10-25
summary: "Implemented capabilities through v0.24.0, grouped by area. The release is a draft; open work is tracked in the roadmap."
---
# Shipped Features

> Current preview: **v0.24.0**, merged into `main`; the [release draft](release-notes/v0.24.0.md) is prepared for publication. Public downloads remain on v0.23.0 until it is published. Toucan targets Windows, macOS and Linux. The WPF app was retired after v0.17.3. Release notes are in [CHANGELOG.md](../CHANGELOG.md);
> open work is in [todos/future-roadmap.md](todos/future-roadmap.md) and open bugs in [known-bugs.md](known-bugs.md).
> This file lists shipped and implemented features only: when something ships, add it here and delete it from the roadmap.
> Unless a line says otherwise, a feature is in the Avalonia app, the app on every platform.

## Core Editor

- [x] Load/save JSON (flat + namespaced)
- [x] Tree view + List view toggle
- [x] Add / Remove / Rename / Duplicate translation IDs
- [x] Add / Remove languages
- [x] Undo / Redo (Ctrl+Z / Ctrl+Y)
- [x] Grouped bulk undo (copy, move to namespace, replace all, accepted pre-translation, and approval batches undo as one step)
- [x] Comment field per translation
- [x] Approved flag (toggle per row)
- [x] Local spell checking with Hunspell and user-managed dictionaries (Avalonia; was WPF only)
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
- [x] Source-first card layout (two columns in wide windows, stacked in narrow windows)
- [x] Focused target metrics: Unicode character count, word count, and translation-memory provenance
- [x] Registered keyboard editing commands: Approve and Next (Ctrl/Cmd+Enter), Next Pending Translation, Accept Suggestion
- [x] Live placeholder source context across edits, linked renames, undo/redo and independent plural variants
- [x] Retained target field focus across card and page boundaries
- [x] Dismissible onboarding guide and first-run setup (default language, theme, optional AI)
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
- [x] Atomic file replacement on save (temporary file, flush, swap); failed saves leave the original untouched
- [x] Journaled multi-file saves: rollback on failure, restore after an interrupted save, previous version kept
- [x] Save never blocked by validation findings; findings shown in the Issues panel
- [x] Saving refuses to overwrite files edited outside Toucan without confirmation
- [x] Recovery drafts of unsaved edits, offered after a crash without overwriting newer disk content
- [x] BabelEdit project import (.babel folders: settings, concepts, comments, approvals, and package mappings into toucan.tproj)
- [x] Exact-resource fidelity and migration for JSON, YAML, Java properties, ARB, RESX, and Laravel PHP
- [x] Spreadsheet interchange with dialect detection (Toucan / BabelEdit), header validation, diff preview, review-state exchange, and batch undo
- [x] ARB placeholder and metadata editor with structured JSON view
- [x] FormatJS generated extraction catalog protection (read-only source catalogs with separate target editing)
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

- [x] Google Translate, DeepL, Microsoft and AI providers (AI goes through AI Integration)
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
- [x] Provider keys kept in an encrypted secret store, never in `providers.json` or a project folder

## AI Integration (since v0.20.0)

- [x] One app-wide AI switch, off by default; first-run onboarding asks whether to use it
- [x] AI services: Claude, OpenAI or a compatible server (Ollama, LM Studio), Gemini; per-service endpoint, model and key; test connection
- [x] Translate with AI (the AI provider: Translation panel, Pre-translate, `toucan translate -p ai`)
- [x] Analyze with AI: wrong terms, tone and meaning in translations, findings in the Issues panel with an Apply fix
- [x] Clarity: source strings a translator could misread, with a clearer wording and a note for translators
- [x] Editable prompts for every AI feature, app-wide or per project (`.toucan/prompts`), with variables and a reset to default
- [x] Per-feature switches and model overrides
- [x] Settings → Data & privacy lists and removes stored secrets
- [x] Migration of the 0.19 Claude, OpenAI and Gemini provider settings and keys

## UI / UX

- [x] Fluent theme with system light/dark (Mica backdrop in WPF)
- [x] Design system tokens (shared ResourceDictionary)
- [x] Framework tile grid for new projects (16 frameworks)
- [x] Start screen with quick actions
- [x] Recent projects flyout (last 10, clear)
- [x] Pin recent projects; choose how many to remember (1–50) and whether Clear keeps pinned ones
- [x] Detect preferred source language from recent projects (opt-in)
- [x] Color schemes: accent presets, custom accent and per-color edits for light and dark, live preview
- [x] Check for updates against GitHub Releases (Stable or Preview channel; never installs)
- [x] Auto-open last project on startup
- [x] Reveal in Explorer
- [x] Centralized KeybindingService (22+ shortcuts)
- [x] Keyboard Shortcuts page in Settings with search, conflict handling, persisted reassignments, and reset
- [x] Settings dialog with 12 pages and two-tier settings (app defaults, per-project overrides)
- [x] Filter: untranslated / translated / approved
- [x] Status bar (project, language, cursor, loading, notifications)
- [x] Notification history flyout in the status bar (retains up to 200 items with severity, source, timestamp, retry save, issues, preferences)
- [x] Unseen notification badge stays cleared after reading history and across validation/project summary refreshes
- [x] Shared custom dialog chrome with caption buttons matching window theme
- [x] Contextual start screen: collapses activity rails and reserved columns until a project is opened
- [x] Project-aware menus and command palette: hides editing, persistence, validation, statistics, and project properties until a project opens
- [x] Plugin toolbar contributions rendered directly in the editor toolbar
- [x] Title bar with logo, save-state chip and command palette pill
- [x] Command palette (Cmd/Ctrl+Shift+P) over every menu command
- [x] Settings search
- [x] Keyboard shortcut sheet (Cmd/Ctrl+/)
- [x] Compact pagination controls
- [x] Editor, Review, and Audit modes; Zen mode (J/K navigation)
- [x] Activity bar with side panels: Explorer, Source Code, Search, Issues, Languages, Inspector, Translation, Memory (Dictionary panel is a placeholder)
- [x] Panel layout saved between sessions

## Search, Bulk Edits, Validation

- [x] Search and replace across keys, values, and languages (match case, whole word, regex, scope, preview, history, highlighted matches)
- [x] Bulk delete, move to namespace, pre-translate, approve, and copy source to target
- [x] Six built-in validation rules: missing, placeholder mismatch, duplicate keys, untranslated (same as source), empty, whitespace mismatch
- [x] Settings editor lists built-in/plugin validation rules and persists enable/severity preferences (runtime enforcement remains APP-06)
- [x] Auto-fix suggestions for common issues (trailing spaces, wrong quotes)

## Translation Memory

- [x] Trigram fuzzy matching with configurable threshold and scope (global or project)
- [x] TMX import and export
- [x] View, delete, and clear stored entries
- [x] Inline ghost-text suggestion from TM in empty fields (Tab to accept)

## Advanced Data Model

- [x] Plural forms support (i18next `_one/_other`, ICU)
- [x] Array support in JSON
- [x] Package support (multiple translation sets)
- [x] Drag & drop reorder in tree
- [x] Multi-selection in tree (batch operations)

## Distribution & Platform

- [x] Avalonia app for macOS (`Toucan.app`) and Linux (tarball), preview since v0.18.0
- [x] Windows packaging scripts: portable EXE, Inno Setup installer, MSIX (`publish.ps1`)
- [x] File association (`.tproj` → open app)
- [x] macOS native menu bar with registry commands, project-aware visibility and native shortcut ownership
- [x] Translation file locations configurable per language
- [x] Encoding (UTF-8, UTF-8 BOM) and line endings (LF, CRLF) per project, from the status bar

## Source Code Integration

- [x] Source code view panel (show where key is used)
- [x] Extract translation IDs from source (`t('key')` patterns)
- [x] Filter: used / unused in source code
- [x] Double-click opens source in external editor
- [x] Wire source root configuration
- [x] Support `.tsx`, `.vue`, `.svelte`, `.py` scanning
- [x] Bounded framework-selected source scanners (i18next, Rails YAML, script profiles)

## Plugins (since v0.18.0; Avalonia app and CLI)

- [x] Plugins add file formats, translation providers, validation rules, and framework profiles from `.dll` assemblies
- [x] Trust model: a plugin loads only when enabled and trusted (SHA-256 of its files)
- [x] `Toucan.Plugins.Abstractions` contract package (plugin API 1.2) and a sample plugin
- [x] Built-in formats, providers, rules and profiles load as compiled-in modules, listed in Settings → Plugins and `toucan plugins list`
- [x] Extension platform: registration vs activation split (application, workspace, and connection scopes)
- [x] Command registry (`ICommandRegistry`) with stable IDs, states, async execution, cancellation, progress, and user shortcuts
- [x] Desktop plugin contributions package (`Toucan.Plugins.Avalonia`): registered panels, inspector sections, settings pages, dialogs, and editor actions
- [x] Host services for plugins (`IPluginServices`): scoped storage, typed settings with schema migrations, secrets, notifications, background operations, and redacted diagnostics
- [x] Controlled workspace API (`IWorkspaceApi`): snapshots, all-or-nothing edit transactions, and undo groups
- [x] Compatibility verification: `minHostVersion`, `platforms`, and settings schema version checks with actionable errors
- [x] SDK test harness (`Toucan.Plugins.Testing`) for host-free plugin unit testing

### Extension platform (since v0.23.0)

- [x] Registration separated from activation: a plugin registers capabilities without network access, then activates per application, workspace or connection with its own session, cancelled and disposed when the project closes; activation failures are reported separately from registration failures
- [x] Command registry with stable IDs, localizable titles and categories, availability states (hidden / unavailable / disconnected / unlicensed), async execution with cancellation and progress, and palette, menu, toolbar and context-menu placements; built-in commands, menus, shortcuts and the chrome buttons all read from it
- [x] User-reassignable shortcuts with conflict detection, replacing the former static keybinding table
- [x] Desktop contribution package (`Toucan.Plugins.Avalonia`, contract 1.0) for side panels, panel toolbars, settings pages, dialogs, inspector sections and editor actions, loaded only by the desktop app so the CLI never loads UI assemblies; the built-in panels register through the same path
- [x] Host services for plugins: scoped settings and writable storage outside the trusted plugin folder, connection secret references, background operations with progress, notifications, redacted diagnostics, and typed configuration fields with validation, defaults, scopes and schema migrations (in-process plugins are not sandboxed)
- [x] Workspace API: readable snapshots and all-or-nothing edit transactions for values, comments, review state and keys, routed through normal undo (one step), dirty tracking, validation and persistence
- [x] Compatibility checks before any plugin code loads: plugin API version, `minHostVersion`, desktop-contract version and platform support, each with an actionable message
- [x] `Toucan.Plugins.Testing` SDK harness (registration, activation, workspace edits, cancellation, CLI-without-desktop check) and a sample REST connector built outside the repository from the published SDK packages only

## Command Line

- [x] `toucan check` (exits 1 on errors, for CI), `stats`, `translate` (with `--dry-run`), `export`, `list-formats`, `list-keys`, `get`, `set`

---

Effort estimates, unfinished work and the order of upcoming work live in [todos/future-roadmap.md](todos/future-roadmap.md).

## Website

- [x] Searchable [release history](changelog.html) generated from `CHANGELOG.md`, with expandable notes, version permalinks, and a shared light/dark theme.
