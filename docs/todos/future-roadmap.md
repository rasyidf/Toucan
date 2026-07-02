# Toucan — Post-1.0 Roadmap

> Tracks features and directions for after the v1.0 stable release.
> Everything here is intentionally deferred to keep 1.0 focused on stability.
> Priorities will shift based on user feedback after launch.

---

## v1.1 — Quality of Life & Distribution

### Auto-Updater
- [ ] Check for updates on startup (configurable: off / notify / auto-install)
- [ ] In-app update download + restart
- [ ] Update channel (stable / preview)

### ConsistencyAI
- [ ] Batch check translations against source language
- [ ] Flag potential mistranslations, tone shifts, missing placeholders
- [ ] Results panel with accept/dismiss per finding
- [ ] Configurable strictness (relaxed / standard / strict)

### Translation Memory Enhancements
- [ ] Embedding-based similarity (upgrade from trigrams)
- [ ] Auto-suggest from TM while typing (inline ghost text)
- [ ] TM import/export (TMX format)
- [ ] Shared TM across team (file-based, no server needed)

### CLI Tool ✅ DONE (v0.15.0)
- [x] `toucan check` — run validation rules, exit code for CI
- [x] `toucan translate` — batch pre-translate from CLI
- [x] `toucan export` — export to any supported format
- [x] `toucan stats` — print translation progress summary

---

## v1.2 — Collaboration & Workflow

### Review Workflow
- [ ] Translation status lifecycle: Draft → Review → Approved → Published
- [ ] Batch approve/reject with comments
- [ ] Review history per key (who approved, when)
- [ ] Filter by status in main view

### Git Integration (StatusBar + UI)
- [ ] Show diff since last commit per key
- [ ] Highlight keys added on current branch
- [ ] Auto-commit on save (opt-in)
- [ ] PR description generator (summarize changes)
- [ ] StatusBar: detect repo via libgit2sharp or Git CLI, show branch name + state
- [ ] StatusBar: file change counts badge (staged/unstaged/ignored)
- [ ] StatusBar: commands for stage/unstage, commit, push/pull
- [ ] StatusBar: clickable areas with tooltips for branch/notifications/warnings
- [ ] StatusBar: notification badge integrated with MessageService

### Webhook Notifications
- [ ] On save: notify external systems (Slack, Teams, custom URL)
- [ ] Configurable per project

### Pretranslation Enhancements
- [ ] Background worker + queue processor for large projects (persistence, job tracking)
- [ ] Audit logs of pre-translations, confidence scoring, review workflows
- [ ] Integration with third-party TMS (Crowdin, Lokalise) and glossary sync

---

## v1.3 — Cross-Platform

### Avalonia Feature Parity
- [ ] Complete all WPF features in Avalonia shell
- [ ] macOS native menu bar integration
- [ ] Linux packaging (AppImage + Flatpak)
- [ ] Shared ViewModel layer (refactor platform-specific code out of VMs)

### Platform-Specific Polish
- [ ] macOS: system accent colors, native file dialogs
- [ ] Linux: respect XDG directories, system theme detection

---

## v2.0 — Extensibility & Scale

### Plugin System
- [ ] Plugin API: load .dll assemblies at runtime
- [ ] Custom format plugins (community-contributed load/save strategies)
- [ ] Custom validation rules (per-project `.toucan/rules/`)
- [ ] Custom providers (beyond built-in + webhook)
- [ ] Plugin manifest + discovery (local folder, no marketplace yet)

### Platform Imports
- [ ] Import from Crowdin (API)
- [ ] Import from Lokalise (API)
- [ ] Import from Phrase (API)
- [ ] Import from Transifex (API)
- [ ] Two-way sync (push/pull)

### Architecture Evolution (if scale demands it)
```
Toucan.Abstractions    — Interfaces only
Toucan.Core            — Models + implementations
Toucan.Formats         — Load/save strategies (hot-loadable via plugins)
Toucan.Providers       — Translation providers (optional, plugin-ready)
Toucan.Analysis        — Validation + Analyzer + TM
Toucan.SourceCode      — Key scanner + git integration
Toucan.Desktop.Wpf     — Windows UI
Toucan.Desktop.Avalonia — Cross-platform UI
Toucan.Cli             — Command-line interface
```

> Only split assemblies if the monolithic Core becomes a build/test bottleneck.
> Don't modularize for the sake of it — do it when there's a real pain point.

---

## Future Considerations (No Timeline)

These are ideas worth tracking but not committed to any version:

### Real-Time Collaboration
- Toucan Hub (minimal server for locking + presence)
- WebSocket-based edit sync
- Conflict resolution UI
- Likely only makes sense if there's strong demand from teams

### Cloud / SaaS
- Project hosting
- Team management + roles
- REST API for automation
- This changes the product category significantly — evaluate carefully

### Advanced AI
- Tone/style enforcement (brand voice profiles)
- Auto-detect placeholders in source, validate in all targets
- Domain-specific glossary enforcement
- Translation quality scoring

### CI/CD Native
- GitHub Action: `toucan-check`
- GitLab CI template
- Fail build on validation errors or missing translations

---

## Guiding Principles for Post-1.0

1. **User feedback drives priority** — ship 1.0, listen, then decide what matters most
2. **Extensibility over features** — prefer plugin hooks over built-in everything
3. **Offline-first stays core** — cloud/collab is additive, never required
4. **Don't over-architect** — split modules when there's real pain, not preemptively
5. **Keep shipping** — small, frequent releases over big-bang versions

---

## Competitive Context

| What Toucan offers | What cloud platforms offer |
|--------------------|---------------------------|
| Free, offline, open-source | Team collaboration, hosted |
| 14 formats, AI analysis | 40-50 formats, integrations |
| Source code scanning | CI/CD built-in |
| Desktop performance | Web accessibility |

The gap is collaboration — bridge it incrementally through file-based workflows (Git, shared TM files) before building server infrastructure. Most solo devs and small teams don't need real-time sync.
