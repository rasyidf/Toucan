# Toucan — Roadmap (v0.17 → v1.0)

> Current release: **v0.19.0** for Windows, macOS and Linux (the WPF app is deprecated; v0.17.3 was its last release). Every build before 1.0 is a preview.
> Feature groups for upcoming sprints. Each group is a self-contained unit of work.
> Pick 2-4 groups per sprint. Items under "Future Plan" are deferred past v1.0.
> The website roadmap ([docs/index.html](../index.html)) and the README summarize this file; update them together.

---

## Recently Completed

### v0.19.0 — Command Palette, Claude and Gemini, UI Polish
- [x] Command palette (Cmd/Ctrl+Shift+P) over every menu command, and a title bar with logo, save state and a centered palette pill
- [x] Claude (Anthropic Messages API) and Gemini translation providers; DeepL free-plan, language-code and formality fixes
- [x] Settings and Project Properties with a left page list and grouped rows; Settings search; plugin cards
- [x] Compact side panels, rebuilt Inspector, Translation and Memory panels, tidied editor toolbar, dark-theme pass
- [x] Headless screenshot harness for every screen ([docs/visual-review.md](../visual-review.md))

### v0.18.0 — First macOS and Linux Release, Plugins
- [x] Avalonia app targets `net10.0`: macOS (`Toucan.app`) and Linux (tarball) builds
- [x] Plugin system, preview (Avalonia app and CLI): formats, providers, validation rules, framework profiles
- [x] `Toucan.Plugins.Abstractions` contract package (plugin API 1.0)
- [x] Formats identified by string IDs; shared `AddToucanCore()` composition root
- [x] Avalonia app: Zen mode, Editor/Review/Audit modes, side panels

### v0.17.3 — Settings Restructure & Packaging
- [x] Two-tier settings (app defaults + per-project overrides), Settings dialog with 12 pages
- [x] Validation, Translation Memory, Source Code, Data & Privacy settings pages
- [x] Packaging scripts: portable EXE, Inno Setup installer, MSIX (`publish.ps1`)

### v0.17.0 — Search, Bulk, Validation, TM (FG-07, FG-08, FG-09, FG-03)
- [x] Search & replace, bulk operations, custom validation rules, TMX import/export

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

- [x] MSIX package build (`publish.ps1 msix`, `packaging/Build-Msix.ps1`)
- [ ] Code signing with self-signed cert (dev) + trusted cert (release)
- [ ] GitHub Releases publish workflow (CI/CD)
- [x] Installer UX (Inno Setup: app icon, start menu, uninstall)
- [x] Portable mode (single-file EXE)
- [x] macOS `.app` (`packaging/build-macos-app.sh`) and Linux tarballs for v0.18.0
- [ ] Scripted Linux packaging (v0.18.0 tarballs were made with `dotnet publish` by hand), AppImage/Flatpak

---

### FG-02: Auto-Updater
**Effort: M | Impact: High | Depends on: FG-01**

- [ ] Check for updates on startup (configurable: off / notify / auto-install)
- [ ] In-app update notification with changelog preview
- [ ] Download + apply update (restart required)
- [ ] Update channel (stable / preview) (setting exists in About; no update check behind it yet)
- [ ] Version check endpoint (GitHub Releases API or custom JSON)

---

### FG-03: Translation Memory Enhancements
**Effort: M | Impact: High**

- [ ] Auto-suggest from TM while typing (inline ghost text): suggestion is computed, editor overlay not built
- [x] TM import/export (TMX format)
- [x] TM entry management (view, delete, clear)
- [x] Per-project vs global TM scope toggle
- [x] Minimum similarity threshold setting

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

> Today: an approved flag per item and a Review mode that approves or rejects. The items below go further.

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
- [x] Show branch name in StatusBar (with changed-file count)
- [ ] Highlight keys changed since last commit
- [ ] Show diff per key (old value vs new)
- [ ] Auto-commit on save (opt-in, configurable message template)
- [ ] Source Control panel: staged/unstaged file list

---

### FG-07: Search & Replace (shipped in v0.17.0)
**Effort: S | Impact: Medium**

- [x] Global search across all keys + values + all languages
- [x] Find & replace with preview (show all matches before applying)
- [x] Regex support
- [x] Scope: current namespace / all / specific languages
- [x] Search history (last 20)

---

### FG-08: Bulk Operations (shipped in v0.17.0)
**Effort: S | Impact: Medium**

- [x] Multi-select keys
- [x] Bulk delete selected keys
- [x] Bulk move to namespace
- [x] Bulk pre-translate selected keys
- [x] Bulk approve/reject selected keys
- [x] Bulk copy source → target language

---

### FG-09: Improved Validation (mostly shipped in v0.17.0)
**Effort: S | Impact: Medium**

- [x] Custom validation rules (regex-based, configurable per project)
- [x] Max length validation (per key or global)
- [x] Forbidden words list
- [x] Validation severity per rule (error vs warning)
- [x] Auto-fix suggestions for common issues (trailing spaces, wrong quotes)
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

- [x] Custom webhook provider for pre-translation
- [x] Configurable URL and auth header
- [ ] Configurable body template
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

- [ ] Unit tests for all Core services (target 80%+ coverage); 275 Core tests today
- [x] Integration tests for load/save round-trip (all 14 formats)
- [ ] ViewModel tests for key workflows (add/remove/translate/validate)
- [ ] GitHub Actions CI pipeline (build + test on push)
- [ ] Code coverage reporting

---

## Future Plan (Post v1.0)

> Nice-to-have and hard items. Not planned for v0.17-v1.0 window.

### Cross-Platform (Avalonia)
- [x] Avalonia app for macOS and Linux (preview since v0.18.0, shares `Toucan.Core` with Windows)
- [ ] Feature parity with the Windows app (missing today: Source Control, Translation and Dictionary panels, among others)
- [ ] Shared ViewModel layer extraction
- [ ] macOS: native menu bar, system accent colors
- [ ] Linux: AppImage + Flatpak packaging, XDG compliance

### Plugin System
> Implemented in the Avalonia app and the CLI (preview). Guide: [docs/plugins.md](../plugins.md). Plan and history: [plugin-system-plan.md](plugin-system-plan.md).
- [x] Plugin API: load .dll assemblies at runtime (`Toucan.Plugins.Abstractions`, isolated load contexts, API versioning)
- [x] Custom format plugins (community load/save strategies, string format IDs)
- [ ] Custom validation rules (per-project `.toucan/rules/`) — plugin rules work, per-project rule files do not
- [x] Custom providers (beyond built-in + webhook)
- [x] Plugin manifest + discovery (`plugin.json`, trust + enable policy, `toucan plugins`)
- [ ] Plugin UI contributions (panels, dialogs, menus) in a separate Avalonia package
- [ ] Mandatory signing and a plugin feed (with the collaboration/auth milestone)
- [ ] Per-rule enable/severity settings for plugin validation rules

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
| v0.17 ✓ | FG-03 + FG-07 + FG-08 + FG-09 | Shipped: search, bulk, validation, TM |
| v0.18 ✓ | Plugins + macOS/Linux | Shipped: first macOS and Linux release, plugin system preview |
| v0.19 ✓ | Palette + providers + UI polish | Shipped: command palette, title bar, Claude and Gemini, UI polish pass |
| v0.20 | FG-10 + rest of FG-03 | Onboarding, ghost-text suggestions |
| v0.21 | FG-04 + FG-15 | AI + Glossary |
| v0.22 | FG-01 + FG-16 | Signed packages, release pipeline, CI |
| v0.23 | FG-05 + FG-06 | Review lifecycle, Git integration |
| v0.24 | FG-02 + FG-11 | Auto-update + Editor |
| v1.0 | FG-13 + stabilization | Performance + first stable release |

> This is a suggestion — pick any order based on what matters most to you.
