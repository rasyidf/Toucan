# Toucan — Roadmap (v0.17 → v1.0)

> Feature groups for upcoming sprints. Each group is a self-contained unit of work.
> Pick 2-4 groups per sprint. Items marked "Future" are deferred past v1.0.

---

## Recently Completed

### v0.16.1 — Panel Extension Polish
- [x] Toolbar removal → TitleBar.TrailingContent
- [x] Inspector split (Languages, Inspector, merged Validation into Issues)
- [x] PanelHost actions slot
- [x] ActivityBar right-click show/hide
- [x] Inspector Fluent redesign (bordered cards)
- [x] ManifestLoadStrategy fallback fix
- [x] ProjectSettings.Save() backfill translationPackages

### v0.16.0 — Panel Extension System
- [x] ISidePanel / SidePanelRegistry / ActivityBar / PanelHost
- [x] 5 left panels (Explorer, Source Code, Search, Issues, Source Control)
- [x] 5 right panels (Languages, Inspector, Translation, Memory, Dictionary)
- [x] Layout persistence (layout.json)

### v0.15.0 — UI Revamp + CLI
- [x] CLI tool (check, stats, translate, export)
- [x] StatusBar panel registry
- [x] ModeSelectorBar
- [x] File association

---

## Available Feature Groups (v0.17 → v1.0)

> Each group can be a sprint. Pick what's most impactful next.

---

### FG-01: MSIX Packaging & Distribution
**Effort: M | Impact: High | Prerequisite for: Auto-Updater**

- [ ] MSIX package build (self-contained, framework-dependent options)
- [ ] Code signing with self-signed cert (dev) + trusted cert (release)
- [ ] GitHub Releases publish workflow (CI/CD)
- [ ] Installer UX (app icon, start menu, uninstall)
- [ ] Portable mode (no install, single folder)

---

### FG-02: Auto-Updater
**Effort: M | Impact: High | Depends on: FG-01**

- [ ] Check for updates on startup (configurable: off / notify / auto-install)
- [ ] In-app update notification with changelog preview
- [ ] Download + apply update (restart required)
- [ ] Update channel (stable / preview)
- [ ] Version check endpoint (GitHub Releases API or custom JSON)

---

### FG-03: Translation Memory Enhancements
**Effort: M | Impact: High**

- [ ] Auto-suggest from TM while typing (inline ghost text)
- [ ] TM import/export (TMX format)
- [ ] TM entry management (view, delete, edit stored entries)
- [ ] Per-project vs global TM scope toggle
- [ ] Minimum similarity threshold setting

---

### FG-04: ConsistencyAI
**Effort: L | Impact: High | Depends on: Provider keys configured**

- [ ] Batch check translations against source language using AI
- [ ] Flag mistranslations, tone shifts, missing placeholders, grammar
- [ ] Results panel with accept/dismiss/re-translate per finding
- [ ] Configurable strictness (relaxed / standard / strict)
- [ ] Provider selection (OpenAI, custom webhook)
- [ ] Progress indicator for batch operations

---

### FG-05: Review Workflow
**Effort: M | Impact: Medium**

- [ ] Translation status lifecycle: Draft → Review → Approved → Published
- [ ] Status stored per key per language (in sidecar or embedded)
- [ ] Batch approve/reject with optional comments
- [ ] Review history per key (who, when, action)
- [ ] Filter by status in main editor view
- [ ] Status badge in translation row

---

### FG-06: Git Integration (Basic)
**Effort: M | Impact: Medium**

- [ ] Detect git repo (libgit2sharp or `git` CLI)
- [ ] Show branch name in StatusBar
- [ ] Highlight keys changed since last commit
- [ ] Show diff per key (old value vs new)
- [ ] Auto-commit on save (opt-in, configurable message template)
- [ ] Source Control panel: staged/unstaged file list

---

### FG-07: Search & Replace
**Effort: S | Impact: Medium**

- [ ] Global search across all keys + values + all languages
- [ ] Find & replace with preview (show all matches before applying)
- [ ] Regex support
- [ ] Scope: current namespace / all / specific languages
- [ ] Search history (last 20)

---

### FG-08: Bulk Operations
**Effort: S | Impact: Medium**

- [ ] Multi-select keys (Ctrl+Click, Shift+Click, Select All)
- [ ] Bulk delete selected keys
- [ ] Bulk move to namespace
- [ ] Bulk pre-translate selected keys
- [ ] Bulk approve/reject selected keys
- [ ] Bulk copy source → target language

---

### FG-09: Improved Validation
**Effort: S | Impact: Medium**

- [ ] Custom validation rules (regex-based, configurable per project)
- [ ] Max length validation (per key or global)
- [ ] Forbidden words list
- [ ] Validation severity per rule (error vs warning)
- [ ] Auto-fix suggestions for common issues (trailing spaces, wrong quotes)
- [ ] Validation on-type (real-time underline, not just on-save)

---

### FG-10: Onboarding & UX Polish
**Effort: S | Impact: Medium**

- [ ] First-run wizard (set default language, provider keys, theme)
- [ ] Tooltip tour for new users (highlight key features)
- [ ] Empty state improvements (all panels show helpful hints when empty)
- [ ] Keyboard shortcut overlay (Ctrl+K Ctrl+S style sheet)
- [ ] Command palette (Ctrl+Shift+P → fuzzy search commands)

---

### FG-11: Editor Improvements
**Effort: S | Impact: Medium**

- [ ] Inline editing (double-click cell → edit in-place in list view)
- [ ] Side-by-side language comparison (2 languages next to each other)
- [ ] Character count + word count in status bar for selected value
- [ ] Auto-resize text area based on content
- [ ] Markdown preview for translations that contain markup

---

### FG-12: Export & Reporting
**Effort: S | Impact: Low-Medium**

- [ ] Translation progress report (exportable as PDF/HTML)
- [ ] Missing translations report per language
- [ ] Unused keys report (from source code scan)
- [ ] Change log export (what changed since date X)
- [ ] Statistics dashboard panel (charts, trends)

---

### FG-13: Performance & Scale
**Effort: M | Impact: Low-Medium (large projects only)**

- [ ] Virtual scrolling for 10k+ keys
- [ ] Lazy loading of language files (load on demand, not all at once)
- [ ] Background save (non-blocking)
- [ ] Indexed search (for fast filter on large datasets)
- [ ] Memory profiling + optimization pass

---

### FG-14: Webhook Provider
**Effort: S | Impact: Low-Medium**

- [ ] Custom webhook provider for pre-translation
- [ ] Configurable URL, headers, body template
- [ ] Response mapping (JSON path to extract translation)
- [ ] Retry logic + timeout settings
- [ ] On-save webhook notification (notify Slack/Teams/custom)

---

### FG-15: Glossary & Terminology
**Effort: M | Impact: Medium**

- [ ] Project glossary (term → translation per language)
- [ ] Glossary enforcement in validation (flag deviations)
- [ ] Auto-suggest from glossary while typing
- [ ] Import/export glossary (CSV, TBX)
- [ ] Dictionary panel shows glossary matches for selected key

---

### FG-16: Test Coverage & CI
**Effort: M | Impact: Low (dev-facing)**

- [ ] Unit tests for all Core services (target 80%+ coverage)
- [ ] Integration tests for load/save round-trip (all 14 formats)
- [ ] ViewModel tests for key workflows (add/remove/translate/validate)
- [ ] GitHub Actions CI pipeline (build + test on push)
- [ ] Code coverage reporting

---

## Future Plan (Post v1.0)

> Nice-to-have and hard items. Not planned for v0.17-v1.0 window.

### Cross-Platform (Avalonia)
- [ ] Complete Avalonia port (macOS + Linux)
- [ ] Shared ViewModel layer extraction
- [ ] macOS: native menu bar, system accent colors
- [ ] Linux: AppImage + Flatpak packaging, XDG compliance

### Plugin System
- [ ] Plugin API: load .dll assemblies at runtime
- [ ] Custom format plugins (community load/save strategies)
- [ ] Custom validation rules (per-project `.toucan/rules/`)
- [ ] Custom providers (beyond built-in + webhook)
- [ ] Plugin manifest + discovery

### Platform Imports
- [ ] Import from Crowdin (API)
- [ ] Import from Lokalise (API)
- [ ] Import from Phrase (API)
- [ ] Import from Transifex (API)
- [ ] Two-way sync (push/pull)

### Real-Time Collaboration
- [ ] Toucan Hub (minimal server for locking + presence)
- [ ] WebSocket-based edit sync
- [ ] Conflict resolution UI

### Advanced AI
- [ ] Embedding-based TM similarity (upgrade from trigrams)
- [ ] Tone/style enforcement (brand voice profiles)
- [ ] Translation quality scoring
- [ ] Domain-specific glossary enforcement via AI

### CI/CD Native
- [ ] GitHub Action: `toucan-check`
- [ ] GitLab CI template
- [ ] Fail build on missing translations threshold

---

## Suggested Sprint Order

For maximum impact toward a v1.0 stable release:

| Sprint | Groups | Theme |
|--------|--------|-------|
| v0.17 | FG-07 + FG-08 + FG-10 | UX completeness |
| v0.18 | FG-03 + FG-09 | Translation quality |
| v0.19 | FG-04 + FG-15 | AI + Glossary |
| v0.20 | FG-01 + FG-16 | Distribution + CI |
| v0.21 | FG-05 + FG-06 | Collaboration basics |
| v0.22 | FG-02 + FG-11 | Auto-update + Editor |
| v1.0 | FG-13 + stabilization | Performance + release |

> This is a suggestion — pick any order based on what matters most to you.
