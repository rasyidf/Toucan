# Toucan Roadmap

> Feature parity tracker vs BabelEdit + Toucan-exclusive improvements.
> Last updated: 2026-06-27

---

## Core Editor

- [x] Load/save JSON (flat + namespaced)
- [x] Tree view + List view toggle
- [x] Add / Remove / Rename / Duplicate translation IDs
- [x] Add / Remove languages
- [x] Undo / Redo (command stack, Ctrl+Z / Ctrl+Y)
- [x] Comment field per translation
- [x] Approved flag (toggle per row)
- [x] Spell checking (WPF native)
- [x] Word-wrapping for long IDs
- [x] Plain text keys mode (no dot-splitting)
- [x] Auto-select newly added ID
- [x] Pagination with compact controls
- [x] Cut / Copy / Paste translation values
- [x] Copy as template (3 configurable `%1` patterns)
- [x] Convert case (lower / upper / sentence / title)
- [x] Remove whitespace (trim / line-by-line / simplify)
- [ ] Tab between edit fields in translation card
- [ ] Infinite scroll mode (alternative to pagination)
- [ ] Filter expression history (remember last N)
- [ ] Statistics dialog (visual grid, per-language)
- [ ] Custom language codes (alias mapping)
- [ ] Better font for RTL languages

## File I/O

- [x] JSON (flat), JSON (namespaced/i18next)
- [x] YAML, TOML, INI
- [x] PO / Gettext
- [x] RESX (.NET), Android XML, iOS .strings
- [x] XLIFF, ARB (Flutter), CSV
- [x] Import / Export menu (all formats)
- [x] Project manifest (`toucan.project` JSON)
- [ ] Excel (.xlsx) import/export
- [ ] Export/Import approved flags + comments in CSV/Excel
- [ ] Java .properties file support (ISO-8859-1)
- [ ] Laravel PHP file support
- [ ] Auto-detect framework on folder drop
- [ ] File watcher (reload changed files from disk)
- [ ] Skip unchanged files on reload

## Machine Translation

- [x] Google Translate, DeepL, Microsoft, OpenAI providers
- [x] Provider settings dialog (API keys, options)
- [x] Translation context passed to providers
- [x] Formality setting (formal / informal → DeepL)
- [x] Remember last translation service
- [x] Preserve parameters in translation (`{{var}}`, `{0}`, `%s`, `:param`)
- [x] Keep uppercase first letter
- [x] Per-language / per-namespace / per-key pre-translate
- [x] Preview before apply
- [ ] ConsistencyAI (check translations against source language)
- [ ] Suggestions panel (fuzzy-match existing translations)
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
- [x] Centralized KeybindingService (20+ shortcuts)
- [x] Keyboard Shortcuts tab in Options dialog
- [x] Filter: untranslated / translated / approved
- [x] Status bar (project, language, cursor, loading, notifications)
- [x] Toucan icon in TitleBar
- [x] Compact pagination controls

## Source Code Integration

- [ ] Source code view panel (show where key is used)
- [ ] Extract translation IDs from source (`t('key')` patterns)
- [ ] Filter: used / unused in source code
- [ ] Double-click opens source in external editor
- [ ] Wire source root configuration (UI exists in Options)
- [ ] Support `.tsx`, `.vue`, `.svelte`, `.py` scanning

## Advanced Data Model

- [ ] Plural forms support (i18next `_one/_other`, ICU)
- [ ] Array support in JSON (`["a","b"]` as indexed children)
- [ ] Package support (multiple translation sets per project)
- [ ] Drag & drop reorder in tree
- [ ] Multi-selection in tree (batch operations)

## Distribution & Platform

- [ ] Avalonia port (macOS + Linux) — started
- [ ] MSIX installer (Windows)
- [ ] Auto-updater (Squirrel or .NET native)
- [ ] File association (`.toucan.project` → open app)
- [ ] Translation file locations configurable per language

---

## Progress

**Done: 54 / 85 (64%)**

**Feature parity vs BabelEdit: ~80%**

---

## Competitive Positioning

### Toucan advantages over BabelEdit
- Open source (no license cost)
- Modern Fluent UI (Mica, system theme)
- More formats (TOML, INI)
- Smarter pre-translate (per-namespace, preview, parameter protection)
- JSON manifest (human-readable)
- Pagination (10k+ keys)
- Centralized keybindings
- Design system tokens

### Remaining gaps vs BabelEdit
- [ ] Source code integration (killer feature)
- [ ] ConsistencyAI
- [ ] Plural forms (i18next/ICU)
- [ ] Excel export
- [ ] Cross-platform (macOS/Linux)

---

## Recommended Next Sprint

1. Tab navigation between edit fields (S)
2. Statistics dialog visual redesign (M)
3. Source code scanning — basic regex key extraction (L)
4. Excel export (M)
5. ConsistencyAI (L)

---

## Effort Legend

- **XS** < 1 hour
- **S** 1–4 hours
- **M** 1–3 days
- **L** 1–2 weeks
- **XL** 1+ month
