# Toucan Roadmap

> Last updated: 2026-06-27

---

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
- [x] Copy as template (3 configurable patterns)
- [x] Convert case (lower / upper / sentence / title)
- [x] Remove whitespace (trim / line-by-line / simplify)
- [x] Tab between edit fields
- [x] Filter expression history (last 15)
- [x] Statistics dialog (visual grid, per-language)
- [ ] Custom language codes (alias mapping)
- [ ] Better font for RTL languages

## Editor Modes

- [x] Focused Editor mode (single-key form, prev/next navigation)
- [x] Zen mode (hides toolbar/statusbar/sidepanel, F11)
- [x] PanelService for centralized panel state
- [ ] Focused Editor: language subset selector
- [ ] Zen mode: keyboard-only navigation (j/k)

## File I/O

- [x] JSON (flat), JSON (namespaced/i18next)
- [x] YAML, TOML, INI
- [x] PO / Gettext
- [x] RESX (.NET), Android XML, iOS .strings
- [x] XLIFF, ARB (Flutter), CSV
- [x] Import / Export menu (all formats)
- [x] Project manifest (`toucan.project` JSON)
- [x] Excel (.xlsx) import/export
- [ ] Export/Import approved flags + comments in Excel
- [ ] Java .properties (ISO-8859-1)
- [ ] Laravel PHP file support
- [ ] Auto-detect framework on folder drop
- [ ] File watcher (reload changed files from disk)
- [ ] Skip unchanged files on reload

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
- [ ] Pre-translate plural forms (i18next/ICU)
- [ ] Translation memory (reuse across projects)

## UI / UX

- [x] WPF UI Fluent (Mica backdrop, system theme)
- [x] Design system tokens (shared ResourceDictionary)
- [x] Framework tile grid for new projects (16 frameworks)
- [x] Start screen with quick actions
- [x] Recent projects flyout (last 10, clear)
- [x] Auto-open last project on startup
- [x] Reveal in Explorer
- [x] Centralized KeybindingService (22+ shortcuts)
- [x] Keyboard Shortcuts tab in Options dialog
- [x] Filter: untranslated / translated / approved
- [x] Status bar (project, language, cursor, loading, notifications)
- [x] Toucan icon in TitleBar
- [x] Compact pagination controls

## Advanced Data Model

- [ ] Plural forms support (i18next `_one/_other`, ICU)
- [ ] Array support in JSON
- [ ] Package support (multiple translation sets)
- [ ] Drag & drop reorder in tree
- [ ] Multi-selection in tree (batch operations)

## Distribution & Platform

- [ ] Avalonia port (macOS + Linux) — started
- [ ] MSIX installer (Windows)
- [ ] Auto-updater
- [ ] File association (`.toucan.project` → open app)
- [ ] Translation file locations configurable per language

## AI Features (Low Priority)

- [ ] ConsistencyAI (check translations against source language)
- [ ] AI-powered translation memory

## Source Code Integration (Low Priority)

- [ ] Source code view panel (show where key is used)
- [ ] Extract translation IDs from source (`t('key')` patterns)
- [ ] Filter: used / unused in source code
- [ ] Double-click opens source in external editor
- [ ] Wire source root configuration
- [ ] Support `.tsx`, `.vue`, `.svelte`, `.py` scanning

---

## Progress

**Done: 62 / 88 (70%)**

**Feature parity vs BabelEdit: ~85%**

---

## Recommended Next Sprint

1. Focused Editor: language subset selector (S)
2. Zen mode: keyboard-only navigation (S)
3. Export/Import approved flags in Excel (S)
4. Plural forms support (L)
5. File watcher — reload changed files (M)

---

## Effort Legend

- **XS** < 1 hour
- **S** 1–4 hours
- **M** 1–3 days
- **L** 1–2 weeks
- **XL** 1+ month
