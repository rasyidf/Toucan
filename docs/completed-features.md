---
title: "Shipped Features"
status: active
updated: 2026-10-07
summary: "Checklist of shipped features as of v0.20.2 (AI Integration added), grouped by area. Only shipped items are listed; open work is in the roadmap and bugs in known-bugs.md."
---
# Shipped Features

> As of v0.20.2 (Windows, macOS and Linux). The WPF app is deprecated; v0.17.3 was its last release. Release notes are in [CHANGELOG.md](../CHANGELOG.md);
> open work is in [todos/future-roadmap.md](todos/future-roadmap.md) and open bugs in [known-bugs.md](known-bugs.md).
> This file lists shipped features only: when something ships, add it here and delete it from the roadmap.
> Unless a line says otherwise, a feature is in the Avalonia app, the app on every platform. Lines marked (WPF) shipped
> only in the WPF app (last release v0.17.3) and are not in the Avalonia app. The Avalonia app does not have the Source
> Control and Dictionary panels yet; its Translation panel is the Machine Translation panel.

## Core Editor

- [x] Load/save JSON (flat + namespaced)
- [x] Tree view + List view toggle
- [x] Add / Remove / Rename / Duplicate translation IDs
- [x] Add / Remove languages
- [x] Undo / Redo (Ctrl+Z / Ctrl+Y)
- [x] Comment field per translation
- [x] Approved flag (toggle per row)
- [x] Spell checking (WPF)
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
- [x] Keyboard Shortcuts page in Settings (reference only, not editable yet)
- [x] Settings dialog with 12 pages and two-tier settings (app defaults, per-project overrides)
- [x] Filter: untranslated / translated / approved
- [x] Status bar (project, language, cursor, loading, notifications)
- [x] Title bar with logo, save-state chip and command palette pill
- [x] Command palette (Cmd/Ctrl+Shift+P) over every menu command
- [x] Settings search
- [x] Keyboard shortcut sheet (Cmd/Ctrl+/)
- [x] Compact pagination controls
- [x] Editor, Review, and Audit modes; Zen mode (J/K navigation)
- [x] Activity bar with side panels: Explorer, Source Code, Search, Issues, Source Control, Languages, Inspector, Translation, Memory (Dictionary panel is a placeholder)
- [x] Panel layout saved between sessions

## Search, Bulk Edits, Validation

- [x] Search and replace across keys, values, and languages (match case, whole word, regex, scope, preview, history, highlighted matches)
- [x] Bulk delete, move to namespace, pre-translate, approve, and copy source to target
- [x] Six built-in validation rules: missing, placeholder mismatch, duplicate keys, untranslated (same as source), empty, whitespace mismatch
- [x] Per-rule enable/disable and severity
- [x] Auto-fix suggestions for common issues (trailing spaces, wrong quotes)
- [x] Custom rules per project: max length, forbidden words, regex, required

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
- [x] Git branch and changed-file count in the status bar
- [x] Translation file locations configurable per language
- [x] Encoding (UTF-8, UTF-8 BOM) and line endings (LF, CRLF) per project, from the status bar


## Source Code Integration

- [x] Source code view panel (show where key is used)
- [x] Extract translation IDs from source (`t('key')` patterns)
- [x] Filter: used / unused in source code
- [x] Double-click opens source in external editor
- [x] Wire source root configuration
- [x] Support `.tsx`, `.vue`, `.svelte`, `.py` scanning

## Plugins (since v0.18.0; Avalonia app and CLI)

- [x] Plugins add file formats, translation providers, validation rules, and framework profiles from `.dll` assemblies
- [x] Trust model: a plugin loads only when enabled and trusted (SHA-256 of its files)
- [x] `Toucan.Plugins.Abstractions` contract package (plugin API 1.0) and a sample plugin
- [x] Built-in formats, providers, rules and profiles load as compiled-in modules, listed in Settings → Plugins and `toucan plugins list`

## Command Line

- [x] `toucan check` (exits 1 on errors, for CI), `stats`, `translate` (with `--dry-run`), `export`, `list-formats`, `list-keys`, `get`, `set`

---

Effort estimates, unfinished work and the order of upcoming work live in [todos/future-roadmap.md](todos/future-roadmap.md).

## Website

- [x] Searchable [release history](changelog.html) generated from `CHANGELOG.md`, with expandable notes, version permalinks, and a shared light/dark theme.
