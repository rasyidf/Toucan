# Toucan Roadmap

> Feature parity tracker vs BabelEdit + Toucan-exclusive improvements.
> Last updated: 2026-06-27

---

## Status Legend

✅ Done — ⏳ In Progress — ❌ Not Started — 🔵 Toucan-exclusive

---

## Completed Features

### Core Editor
- ✅ Load/save JSON (flat + namespaced)
- ✅ Tree view + List view toggle
- ✅ Add / Remove / Rename / Duplicate translation IDs
- ✅ Add / Remove languages
- ✅ Undo / Redo (command stack, Ctrl+Z / Ctrl+Y)
- ✅ Comment field per translation
- ✅ Approved flag (toggle per row)
- ✅ Spell checking (WPF native)
- ✅ Word-wrapping for long IDs
- ✅ Plain text keys mode (no dot-splitting)
- ✅ Auto-select newly added ID
- ✅ Pagination with compact controls
- ✅ Cut / Copy / Paste translation values
- ✅ Copy as template (3 configurable `%1` patterns)
- ✅ Convert case (lower / upper / sentence / title)
- ✅ Remove whitespace (trim / line-by-line / simplify)

### File I/O (12 formats)
- ✅ JSON (flat), JSON (namespaced/i18next)
- ✅ YAML, TOML, INI (🔵)
- ✅ PO / Gettext
- ✅ RESX (.NET), Android XML, iOS .strings
- ✅ XLIFF, ARB (Flutter), CSV
- ✅ Import / Export menu (all formats)
- ✅ Project manifest (`toucan.project` JSON) (🔵)

### Machine Translation
- ✅ Google Translate, DeepL, Microsoft, OpenAI providers
- ✅ Provider settings dialog (API keys, options)
- ✅ Translation context passed to providers
- ✅ Formality setting (formal / informal → DeepL)
- ✅ Remember last translation service
- ✅ Preserve parameters in translation (`{{var}}`, `{0}`, `%s`, `:param`)
- ✅ Keep uppercase first letter
- ✅ Per-language / per-namespace / per-key pre-translate (🔵)
- ✅ Preview before apply (🔵)

### UI / UX
- ✅ WPF UI Fluent (Mica backdrop, system theme) (🔵)
- ✅ Design system tokens (shared ResourceDictionary) (🔵)
- ✅ Framework tile grid for new projects (16 frameworks)
- ✅ Start screen with quick actions
- ✅ Recent projects flyout (last 10, clear)
- ✅ Auto-open last project on startup
- ✅ Reveal in Explorer
- ✅ Centralized KeybindingService (20+ shortcuts)
- ✅ Keyboard Shortcuts tab in Options dialog
- ✅ Filter: untranslated / translated / approved
- ✅ Status bar (project, language, cursor, loading, notifications)

---

## Phase 1 — Editor Refinement (Next)

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 1.1 | Tab between edit fields in translation card | S | High |
| 1.2 | Infinite scroll mode (alternative to pagination) | M | Medium |
| 1.3 | Filter expression history (remember last N) | S | Medium |
| 1.4 | Statistics dialog (visual grid, per-language) | M | Medium |
| 1.5 | Custom language codes (alias mapping) | S | Low |
| 1.6 | Better font for RTL languages | S | Low |

---

## Phase 2 — Source Code Integration

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 2.1 | Source code view panel (show where key is used) | L | High |
| 2.2 | Extract translation IDs from source (`t('key')` patterns) | L | High |
| 2.3 | Filter: used / unused in source code | M | High |
| 2.4 | Double-click opens source in external editor | S | Medium |
| 2.5 | Wire source root configuration (already in Options UI) | S | Medium |
| 2.6 | Support `.tsx`, `.vue`, `.svelte`, `.py` scanning | M | Medium |

---

## Phase 3 — AI & Translation Intelligence

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 3.1 | ConsistencyAI (check translations against source language) | L | High |
| 3.2 | Suggestions panel (fuzzy-match existing translations) | M | High |
| 3.3 | Pre-translate plural forms (i18next/ICU) | M | Medium |
| 3.4 | Translation memory (reuse across projects) | L | Low |

---

## Phase 4 — Advanced Data Model

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 4.1 | Plural forms support (i18next `_one/_other`, ICU) | L | High |
| 4.2 | Array support in JSON (`["a","b"]` as indexed children) | M | Medium |
| 4.3 | Package support (multiple translation sets per project) | L | Medium |
| 4.4 | Drag & drop reorder in tree | M | Low |
| 4.5 | Multi-selection in tree (batch operations) | M | Low |

---

## Phase 5 — Import/Export & Interop

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 5.1 | Excel (.xlsx) import/export | M | High |
| 5.2 | Export/Import approved flags + comments in CSV/Excel | S | Medium |
| 5.3 | Java .properties file support (ISO-8859-1) | S | Medium |
| 5.4 | Laravel PHP file support | M | Low |
| 5.5 | Auto-detect framework on folder drop | S | Medium |
| 5.6 | File watcher (reload changed files from disk) | M | Medium |
| 5.7 | Skip unchanged files on reload | S | Low |

---

## Phase 6 — Distribution & Platform

| # | Feature | Effort | Priority |
|---|---------|--------|----------|
| 6.1 | Avalonia port (macOS + Linux) | XL | High |
| 6.2 | MSIX installer (Windows) | M | High |
| 6.3 | Auto-updater (Squirrel or .NET native) | M | Medium |
| 6.4 | File association (`.toucan.project` → open app) | S | Medium |
| 6.5 | Translation file locations configurable per language | M | Low |

---

## Progress Summary

| Area | Done | Remaining |
|------|------|-----------|
| Core Editor | 16 | 6 |
| File I/O | 14 | 5 |
| Machine Translation | 10 | 4 |
| UI / UX | 14 | 0 |
| Source Code | 0 | 6 |
| Advanced Data | 0 | 5 |
| Distribution | 0 | 5 |
| **Total** | **54** | **31** |

**Feature parity: ~80% of BabelEdit achieved.**

---

## Competitive Positioning

### Already better than BabelEdit
| Toucan Advantage | Detail |
|-----------------|--------|
| Open source | No license cost, community contributions |
| Modern UI | Fluent WPF, Mica, system theme auto |
| More formats | TOML, INI not in BabelEdit |
| Smarter pre-translate | Per-namespace, preview-before-apply, parameter protection |
| JSON manifest | Human-readable vs BabelEdit's opaque `.babel` XML |
| Pagination | Handles 10k+ keys without UI freeze |
| Design system | Consistent, maintainable styling |
| Centralized keybindings | Single source of truth, visible in Options |

### BabelEdit still leads on
| Gap | Impact | Effort to close |
|-----|--------|----------------|
| Source code integration | High — killer workflow feature | L (2 weeks) |
| ConsistencyAI | High — unique AI value | L (2 weeks) |
| Plural forms | Medium — i18next/ICU users need it | L (2 weeks) |
| Excel export | Medium — translator handoff | M (3 days) |
| Cross-platform | High — macOS/Linux users | XL (ongoing) |

---

## Recommended Next Sprint

1. **Tab navigation** between edit fields (S) — quick UX win
2. **Statistics dialog** visual redesign (M) — looks amateur currently
3. **Source code scanning** (L) — start with basic regex key extraction
4. **Excel export** (M) — unblocks translator workflows
5. **ConsistencyAI** (L) — competitive differentiator

---

## Effort Legend

| Code | Time | Example |
|------|------|---------|
| XS | < 1 hour | Config flag, one-liner |
| S | 1–4 hours | Single feature, single file |
| M | 1–3 days | Multi-file, needs design |
| L | 1–2 weeks | New subsystem |
| XL | 1+ month | Major initiative |
