# Toucan Roadmap — Feature Parity with BabelEdit + Improvements

> Synthesized from BabelEdit 1.0–5.6 release history vs Toucan's current state.
> Status: ✅ Done | 🟡 Partial | ❌ Not started | 🔵 Toucan-only (improvement over BabelEdit)
> Last updated: 2026-06-27

---

## Phase 0 — Foundation (Complete ✅)

| Feature | BabelEdit | Toucan | Status |
|---------|-----------|--------|--------|
| Load/save JSON (flat + namespaced) | ✅ v0.9 | ✅ | ✅ |
| Tree view + List view toggle | ✅ v1.0 | ✅ | ✅ |
| Add/Remove/Rename translation IDs | ✅ v0.9 | ✅ | ✅ |
| Add/Remove languages | ✅ v0.9 | ✅ | ✅ |
| Recent files list (flyout, last 10) | ✅ v0.9 | ✅ | ✅ |
| Start screen | ✅ v0.9 | ✅ | ✅ |
| Pagination / virtual scroll | ❌ (scroll-extend) | ✅ | 🔵 |
| YAML support | ✅ v1.9 | ✅ | ✅ |
| PO/Gettext support | ✅ v5.3 | ✅ | ✅ |
| RESX (.NET) support | ✅ v4.1 | ✅ | ✅ |
| Android XML support | ✅ v2.7 | ✅ | ✅ |
| iOS .strings support | ✅ v2.6 | ✅ | ✅ |
| XLIFF support | ✅ v2.7 | ✅ | ✅ |
| ARB/Flutter support | ✅ v2.6 | ✅ | ✅ |
| CSV export/import | ✅ v1.0 | ✅ | ✅ |
| TOML support | ❌ | ✅ | 🔵 |
| INI support | ❌ | ✅ | 🔵 |
| Import/Export menu | ✅ v1.0 | ✅ | ✅ |
| Pre-translate (machine translation) | ✅ v1.6 | ✅ | ✅ |
| Google Translate provider | ✅ v1.6 | ✅ | ✅ |
| DeepL provider | ✅ v2.8 | ✅ | ✅ |
| Microsoft Translator | ✅ v2.8 | ✅ | ✅ |
| OpenAI provider | ✅ v5.2 | ✅ | ✅ |
| Provider settings dialog | ✅ | ✅ | ✅ |
| Convert case (upper/lower/sentence/title) | ✅ v3.0 | ✅ | ✅ |
| Remove whitespace (trim/simplify) | ✅ v3.0 | ✅ | ✅ |
| Framework tile selection (new project) | ✅ v0.9 | ✅ | ✅ |
| Project manifest file | ❌ (.babel XML) | ✅ (toucan.project JSON) | 🔵 |
| WPF UI Fluent design | ❌ (custom Qt) | ✅ | 🔵 |
| Dark mode | ✅ v1.5 | ✅ (system) | ✅ |
| Comment field per translation ID | ✅ v1.0 | ✅ | ✅ |
| Approved flag per translation | ✅ v1.0 | ✅ | ✅ |
| Filter: translated/untranslated/approved | ✅ v1.0 | ✅ | ✅ |
| Auto-select newly added ID | ✅ v4.0.2 | ✅ | ✅ |
| Auto-open last project on startup | ✅ v5.1.1 | ✅ | ✅ |
| Spell checking | ✅ v1.2 | ✅ | ✅ |
| Reveal in Explorer | ✅ v2.8 | ✅ | ✅ |
| Duplicate ID/folder | ✅ v2.4 | ✅ | ✅ |
| Copy template snippets | ✅ v0.9.2 | ✅ | ✅ |
| Translation context (DeepL, OpenAI) | ✅ v5.4.1 | ✅ | ✅ |
| Formality setting (formal/informal) | ✅ v5.4.1 | ✅ | ✅ |
| Remember last translation service | ✅ v5.1.1 | ✅ | ✅ |
| Word-wrapping for long IDs | ✅ v2.9 | ✅ | ✅ |
| Cut/Copy/Paste translation IDs | ✅ v1.2 | ✅ | ✅ |
| Design system / shared resources | ❌ | ✅ | 🔵 |

---

## Phase 1 — Core Editor Polish (Next Sprint)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 1.1 | **Undo/Redo** | v0.9 | M | ❌ |
| 1.9 | **Scroll-extend / virtual scroll** (infinite scroll mode) | v4.0.3 | M | ❌ |
| 1.10 | **Keyboard shortcut: Tab between edit fields** | v0.9.2 | S | ❌ |

---

## Phase 2 — Source Code Integration (Q4 2026)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 2.1 | **Source code view** (show where key is used) | v5.1.1 | L | ❌ |
| 2.2 | **Extract translation IDs from source code** | v5.1.1 | L | ❌ |
| 2.3 | **Filter: used/unused in source** | v5.1.1 | M | ❌ |
| 2.4 | **Double-click opens source in external editor** | v5.1.1 | S | ❌ |
| 2.5 | **Source root configuration** | v2.3 | S | 🟡 (UI exists, not wired) |

---

## Phase 3 — Machine Translation Enhancements (Q1 2027)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 3.3 | **ConsistencyAI** (check translations against source) | v5.0 | L | ❌ |
| 3.4 | **Suggestions panel** (show similar translations) | v1.6 | M | ❌ |
| 3.6 | **Preserve parameters in translation** (`:param`, `{{var}}`) | v2.6 | M | ❌ |
| 3.7 | **Keep uppercase first letter** | v4.0.2 | XS | ❌ |
| 3.8 | **Pre-translate plural forms** | v5.5.1 | M | ❌ |

---

## Phase 4 — Advanced Editing (Q2 2027)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 4.3 | **Drag & drop reorder** in tree | v3.0 | M | ❌ |
| 4.5 | **Multi-selection in tree** (expand/collapse with arrows) | v4.0.2 | M | ❌ |
| 4.6 | **Package support** (multiple independent translation sets) | v4.0 | L | ❌ |
| 4.7 | **Array support in JSON** | v1.7 | M | ❌ |
| 4.8 | **Plural forms** (i18next, ICU) | v5.5.1 | L | ❌ |
| 4.9 | **Plain text keys** (don't split at dot) | v1.7 | S | ❌ |

---

## Phase 5 — Import/Export & Interop (Q3 2027)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 5.1 | **Excel (.xlsx) import/export** | v2.1 | M | ❌ |
| 5.2 | **Export/Import Approved flags + comments** | v5.2 | S | ❌ |
| 5.3 | **Java .properties file support** | v2.1 | S | ❌ |
| 5.4 | **Laravel PHP file support** | v1.1 | M | ❌ |
| 5.5 | **Auto-detect framework on drop** | v1.5 | S | ❌ |
| 5.6 | **Reload changed files from disk** | v4.0.3 | M | ❌ |
| 5.7 | **Don't reload unchanged files** | v4.0.2 | S | ❌ |

---

## Phase 6 — Polish & Professional UX (Q4 2027)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 6.1 | **Statistics dialog** (proper visual, per-language breakdown) | v2.0 | M | ❌ |
| 6.2 | **Filter expression history** | v2.3 | S | ❌ |
| 6.5 | **Custom language codes** | v3.0 | S | ❌ |
| 6.6 | **Translation file locations configurable** | v3.0 | M | ❌ |
| 6.9 | **Better font for RTL languages** | v2.6 | S | ❌ |

---

## Phase 7 — Cross-Platform & Distribution (2028+)

| # | Feature | BabelEdit ref | Effort | Status |
|---|---------|---------------|--------|--------|
| 7.1 | **Avalonia port** (macOS + Linux) | Multi-platform | XL | 🟡 (started) |
| 7.2 | **MSIX installer** (Windows) | MSI | M | ❌ |
| 7.3 | **Auto-updater** | v0.9 | M | ❌ |
| 7.4 | **File association** (.toucan.project → open app) | v1.2 | S | ❌ |
| 7.5 | **Licensing system** | BabelEdit commercial | L | N/A (OSS) |

---

## Toucan-Only Improvements (Not in BabelEdit)

| Feature | Description |
|---------|-------------|
| 🔵 **Modern Fluent UI** | WPF UI with Mica backdrop, system theme integration |
| 🔵 **TOML / INI support** | Formats BabelEdit doesn't support |
| 🔵 **Open-source** | Community contributions, no license cost |
| 🔵 **JSON project manifest** | Human-readable `toucan.project` vs BabelEdit's opaque `.babel` XML |
| 🔵 **Pagination** | Better performance for massive projects (10k+ keys) |
| 🔵 **Per-language context menu** | Quick pre-translate/stats/hide per language without dialog |
| 🔵 **Plugin-ready architecture** | DI-based, strategy pattern for formats = easy to extend |
| 🔵 **AI-powered batch operations** | Multiple AI providers with streaming support |
| 🔵 **Namespace-scoped pre-translate** | Translate a subtree, not just all-or-nothing |
| 🔵 **Design system tokens** | Shared ResourceDictionary for consistent spacing/styling |

---

## Progress Summary

- **Phase 0**: 40/40 items complete ✅
- **Phase 1**: 0/3 remaining
- **Phase 2**: 0/5 remaining
- **Phase 3**: 0/5 remaining
- **Phase 4**: 0/6 remaining
- **Phase 5**: 0/7 remaining
- **Phase 6**: 0/5 remaining
- **Phase 7**: 0/5 remaining

**Total feature parity achieved**: ~75% of BabelEdit's feature set implemented.
**Remaining gap**: Source code integration, Undo/Redo, plural forms, Excel, ConsistencyAI.

---

## Effort Legend

- **XS** = < 1 hour (one-liner or config change)
- **S** = 1–4 hours (single feature, single file)
- **M** = 1–3 days (multi-file, needs design)
- **L** = 1–2 weeks (new subsystem)
- **XL** = 1+ month (major initiative)

---

## Next Priority (Recommended Order)

1. **Undo/Redo (1.1)** — Most impactful editor feature still missing
2. **ConsistencyAI (3.3)** — Competitive differentiator
3. **Preserve parameters (3.6)** — Prevents broken translations
4. **Excel import/export (5.1)** — Translator workflow requirement
5. **Source code view (2.1)** — BabelEdit's killer feature
