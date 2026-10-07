---
title: "Toucan — Roadmap (v0.17 to v1.0)"
status: in-progress
progress: "81 open items"
updated: 2026-10-07
summary: "Open work only, in feature groups FG-01 to FG-16 plus post-1.0 ideas, with a suggested sprint order. Shipped items are removed, not ticked. Next: v0.20 onboarding and ghost-text suggestions."
---
# Toucan — Roadmap (v0.17 → v1.0)

> Current release: **v0.19.0** for Windows, macOS and Linux. Every build before 1.0 is a preview.

**This file lists open work only.** A shipped item is deleted from here, not ticked. Where shipped work goes:

| When an item ships | Do this |
|---|---|
| Any change users can see | Add a line to `CHANGELOG.md` under `[Unreleased]` (the history) |
| A lasting capability | Add a line to [completed-features.md](../completed-features.md) (what the app does now) |
| A group has no open items left | Delete the group and its row in the sprint table |
| A bug is fixed | Delete it from [known-bugs.md](../known-bugs.md); the changelog keeps it |

Bugs and gaps that already have an ID in [known-bugs.md](../known-bugs.md) are linked by ID here, not described twice. The website roadmap ([docs/index.html](../index.html)) and the README table summarize this file; update them together.

Pick 2-4 groups per sprint. Items under "Future Plan" are deferred past v1.0.

---

## Open Feature Groups

### FG-01: Packaging & Distribution
**Effort: M | Impact: High | Prerequisite for: Auto-Updater | Bugs: REL-01, REL-02, REL-04**

- [ ] Code signing with self-signed cert (dev) + trusted cert (release)
- [ ] GitHub Releases publish workflow (CI/CD)
- [ ] Scripted Linux packaging (v0.18.0 tarballs were made with `dotnet publish` by hand), AppImage/Flatpak

---

### FG-02: Auto-Updater
**Effort: M | Impact: High | Depends on: FG-01 | Bugs: REL-03**

- [ ] Check for updates on startup (configurable: off / notify / auto-install)
- [ ] In-app update notification with changelog preview
- [ ] Download + apply update (restart required)
- [ ] Update channel (stable / preview) (setting exists in About; no update check behind it yet)
- [ ] Version check endpoint (GitHub Releases API or custom JSON)

---

### FG-03: Translation Memory Enhancements
**Effort: M | Impact: High**

- [ ] Auto-suggest from TM while typing (inline ghost text): suggestion is computed, editor overlay not built

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
- [ ] Highlight keys changed since last commit
- [ ] Show diff per key (old value vs new)
- [ ] Auto-commit on save (opt-in, configurable message template)
- [ ] Source Control panel: staged/unstaged file list

---

### FG-09: Improved Validation
**Effort: S | Impact: Medium**

- [ ] Validation on-type (real-time underline, not just on-save)

---

### FG-10: Onboarding & UX Polish
**Effort: S | Impact: Medium**

- [ ] First-run wizard (set default language, provider keys, theme)
- [ ] Tooltip tour for new users (highlight key features)
- [ ] Empty state improvements (all panels show helpful hints when empty)
- [ ] Keyboard shortcut overlay (Ctrl+K Ctrl+S style sheet)

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
**Effort: M | Impact: Low-Medium (large projects only) | Bugs: QA-02**

- [ ] Virtual scrolling for 10k+ keys
- [ ] Lazy loading of language files (load on demand, not all at once)
- [ ] Background save (non-blocking)
- [ ] Indexed search (for fast filter on large datasets)
- [ ] Memory profiling + optimization pass

---

### FG-14: Webhook Provider
**Effort: S | Impact: Low-Medium**

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
**Effort: M | Impact: Low (dev-facing) | Bugs: QA-01, REL-01**

- [ ] Unit tests for all Core services (target 80%+ coverage); 275 Core tests today
- [ ] ViewModel tests for key workflows (add/remove/translate/validate)
- [ ] GitHub Actions CI pipeline (build + test on push)
- [ ] Code coverage reporting

---

## Future Plan (Post v1.0)

> Nice-to-have and hard items. Not planned for v0.17-v1.0 window.

### Cross-Platform (Avalonia)
- [ ] Source Control and Dictionary panels (APP-01)
- [ ] macOS: native menu bar, system accent colors
- [ ] Linux: AppImage + Flatpak packaging, XDG compliance

### Plugin System
> Core plugin system shipped in v0.18.0 (Avalonia app and CLI, preview). Guide: [docs/plugins.md](../plugins.md). Plan and history: [plugin-system-plan.md](../archive/plugin-system-plan.md).
- [ ] Custom validation rules (per-project `.toucan/rules/`) — plugin rules work, per-project rule files do not
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
| v0.20 | FG-10 + FG-03 | Onboarding, ghost-text suggestions |
| v0.21 | FG-04 + FG-15 | AI + Glossary |
| v0.22 | FG-01 + FG-16 | Signed packages, release pipeline, CI |
| v0.23 | FG-05 + FG-06 | Review lifecycle, Git integration |
| v0.24 | FG-02 + FG-11 | Auto-update + Editor |
| v1.0 | FG-13 + stabilization | Performance + first stable release |

> This is a suggestion — pick any order based on what matters most to you.
