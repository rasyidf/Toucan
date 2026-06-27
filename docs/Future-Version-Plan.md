# Toucan v1.0 — Future Version Plan

> Current state: v0.6.1, 88/88 roadmap features, ~95% BabelEdit parity
> Codebase: 249 C# files, 629 KB source, 3 projects (Core: 102 files, WPF: 85, Avalonia: 57)
> Last sprint: +3,099 lines across 59 files

---

## What We Have (v0.6.1)

### Architecture
```
Toucan.Core (102 files)         — Format-agnostic shared library
├── Contracts/                  — 10 interfaces (strategy, provider, validation, analyzer, TM, source code)
├── Models/                     — TranslationItem, ProjectSettings, NsTreeItem, etc.
├── Services/
│   ├── Frameworks/ (8)         — Profile-based project detection
│   ├── LoadStrategies/ (14)    — Format parsers
│   ├── SaveStrategies/ (14)    — Format writers
│   ├── Providers/ (6)          — Translation providers (Google, DeepL, Microsoft, OpenAI, Custom, Mock)
│   ├── Validation/ (7)         — Pre-save quality rules
│   ├── SourceCodeService       — Key usage scanner
│   ├── TranslationAnalyzer     — LLM-powered domain analysis
│   ├── TranslationMemory       — Cross-project phrase reuse
│   └── PackageService          — Multi-set management

Toucan (WPF, 85 files)          — Windows desktop UI
├── ViewModels/                 — MVVM (CommunityToolkit.Mvvm)
├── Views/                      — WPF-UI Fluent controls
└── Services/                   — Platform-specific (dialogs, keybindings, undo/redo)

Toucan.Avalonia (57 files)      — Cross-platform UI (in progress)
```

### Feature Coverage
- ✅ 14 file formats (JSON flat/nested, YAML, TOML, INI, PO, RESX, Android XML, iOS .strings, XLIFF, ARB, CSV, Java .properties, Laravel PHP)
- ✅ 8 framework profiles with auto-detection
- ✅ 6 translation providers (Google, DeepL, Microsoft, OpenAI, Custom webhook, Mock)
- ✅ 6 validation rules (pre-save pipeline)
- ✅ Source code integration (scan, filter used/unused, open in editor)
- ✅ Translation memory (cross-project, trigram fuzzy match)
- ✅ Context-aware translation analyzer (LLM-powered domain terminology check)
- ✅ Package support (multiple translation sets)
- ✅ Undo/redo, pagination, keyboard shortcuts, dark/light theme, Fluent UI

---

## v1.0 Release — Stabilization (4-6 weeks)

Before 1.0, the codebase needs cleanup to go from "all features exist" to "all features are polished":

### Code Quality
- [ ] Remove dead code paths and unused parameters (60+ analyzer warnings)
- [ ] Nullable annotations audit (Toucan.Core has `enable`, WPF has `warnings` — make consistent)
- [ ] Extract remaining `using Newtonsoft.Json` → `System.Text.Json` (reduce deps)
- [ ] Move WPF `Toucan/Services/` interfaces to Core contracts (currently duplicated)
- [ ] Unit test coverage for Core services (target: 80% line coverage)
- [ ] Integration tests for Load/Save round-trip (all 14 formats)

### Performance
- [ ] Profile memory usage on 10K+ key projects
- [ ] Lazy-load tree nodes (currently loads all into memory)
- [ ] Debounce validation pipeline on rapid edits
- [ ] Cache translation memory index (avoid full scan on every search)

### UX Polish
- [ ] Validation results panel (dedicated pane, not just save-time dialog)
- [ ] Source code usages panel (show references for selected key)
- [ ] Analysis results panel (show analyzer findings inline)
- [ ] Translation memory suggestions in edit field (autocomplete popup)
- [ ] Package selector in toolbar (switch between packages)
- [ ] Drag-drop visual feedback in tree (drop target highlight)
- [ ] Keyboard shortcut for analyze (Ctrl+Shift+A)

### Documentation
- [ ] In-app help (F1 → opens docs)
- [ ] Provider setup guides (per-provider: where to get API key, what to set)
- [ ] Framework profile guides (what each profile expects, folder conventions)

---

## v1.1 — Collaboration & Team Features

### Real-Time Collaboration (WebSocket)
- [ ] Lock key on edit (prevent concurrent modification)
- [ ] Show who's editing what (presence indicators)
- [ ] Conflict resolution UI (merge dialog)
- [ ] Server component (minimal — Toucan Hub)

### Review Workflow
- [ ] Translation status: Draft → Review → Approved → Published
- [ ] Reviewer role (can approve but not edit)
- [ ] Batch approve/reject with comments
- [ ] Review history per key

### Git Integration (beyond scan)
- [ ] Show diff since last commit per key
- [ ] Auto-commit on save (configurable)
- [ ] Branch-aware: highlight keys added on current branch
- [ ] PR description generator (summarize translation changes)

---

## v1.2 — Intelligence & Automation

### Advanced AI
- [ ] Translation memory powered by embeddings (not just trigrams)
- [ ] Auto-suggest translations from TM while typing
- [ ] Batch consistency check across entire project (not just on-demand)
- [ ] Tone/style enforcement (brand voice: formal, friendly, technical)
- [ ] Auto-detect placeholders in source and validate in all targets

### Automation
- [ ] CLI tool (`toucan check`, `toucan translate`, `toucan export`)
- [ ] CI/CD integration (fail build on validation errors)
- [ ] Watch mode (auto-reload + auto-validate on file changes)
- [ ] Webhook on save (notify external systems: Slack, Teams, custom)

### Import from Platforms
- [ ] Import from Crowdin (API)
- [ ] Import from Lokalise (API)
- [ ] Import from Phrase (API)
- [ ] Import from Transifex (API)
- [ ] Two-way sync with platform (push/pull)

---

## v2.0 — Cross-Platform & Cloud

### Avalonia Feature Parity
- [ ] Complete all WPF features in Avalonia
- [ ] macOS native look (system menubar, touch bar)
- [ ] Linux package (AppImage, Flatpak)
- [ ] Shared ViewModel layer (refactor WPF-specific out)

### Toucan Cloud (optional SaaS)
- [ ] Project hosting (store translations in cloud)
- [ ] Team management (invite translators)
- [ ] API access (REST + GraphQL)
- [ ] Translation ordering (assign keys to translators)
- [ ] Payment/billing for professional translators

### Plugin System
- [ ] Plugin API (load .dll plugins at runtime)
- [ ] Custom format plugins (community-contributed)
- [ ] Custom validation rules (per-project)
- [ ] Custom providers (beyond HTTP webhook)
- [ ] Plugin marketplace / registry

---

## Architecture Evolution

### Current Technical Debt
| Area | Issue | Fix |
|------|-------|-----|
| MainWindowViewModel | 51KB monolith, 1300+ lines | Split into partial classes or sub-VMs |
| Duplicate service interfaces | WPF `IDialogService` vs Core contracts | Move to Core, make platform-agnostic |
| Newtonsoft.Json | Used in WPF services alongside System.Text.Json in Core | Consolidate to STJ |
| Static `App.Services` | ViewModels reach into DI container directly | Constructor injection everywhere |
| Translation loading | Full in-memory, no streaming | IAsyncEnumerable for large projects |
| Provider config wiring | BuildProviderOptions() reads from App.Services | Inject resolved config into providers |

### Proposed Module Split (for v2.0)
```
Toucan.Abstractions    — Interfaces only (ILoadStrategy, ITranslationProvider, etc.)
Toucan.Core            — Models + implementations + no platform deps
Toucan.Formats         — All load/save strategies (separate assembly, hot-loadable)
Toucan.Providers       — Translation providers (separate, optional)
Toucan.Analysis        — Validation + Analyzer + TM (separate, optional)
Toucan.SourceCode      — Key scanner + git integration
Toucan.Desktop.Wpf     — WPF UI layer
Toucan.Desktop.Avalonia — Avalonia UI layer
Toucan.Cli             — Command-line interface
Toucan.Server          — Optional collaboration server
```

---

## Immediate Priorities (Next 2 Weeks)

1. **Unit tests** — Core services, especially Load/Save round-trips
2. **MainWindowViewModel split** — Extract into command groups (file ops, edit ops, translate ops)
3. **Remove Newtonsoft.Json** from WPF project — only STJ
4. **Validation results panel** — real UI for viewing/filtering issues
5. **MSIX packaging** — create installer for distribution

---

## Metrics & Goals

| Metric | Current | v1.0 Target |
|--------|---------|-------------|
| Test coverage (Core) | ~15% | 80% |
| Build time | 12s | < 8s |
| Startup time | ~3s | < 1.5s |
| Memory (10K keys) | unmeasured | < 200MB |
| NuGet deps (WPF) | 12 | < 8 |
| Analyzer warnings | 60+ | 0 |

---

## Competitive Landscape (2026)

| Feature | BabelEdit | Toucan | Crowdin | Phrase |
|---------|-----------|--------|---------|--------|
| Price | $119/yr | Free (OSS) | $120+/mo | $25+/mo |
| Offline | ✅ | ✅ | ❌ | ❌ |
| Formats | 12 | 14 | 50+ | 40+ |
| AI Translation | ❌ | ✅ (4 providers) | ✅ | ✅ |
| Context Analysis | ❌ | ✅ | ❌ | ❌ |
| Source Code Scan | ✅ | ✅ | ❌ | ❌ |
| Translation Memory | ❌ | ✅ | ✅ | ✅ |
| Cross-Platform | ✅ (Electron) | 🟡 (Avalonia WIP) | Web | Web |
| Plugin System | ❌ | Planned | ✅ | ✅ |
| Team/Cloud | ❌ | Planned | ✅ | ✅ |
| Validation | Basic | 6 rules + AI | Basic | Basic |
| Open Source | ❌ | ✅ | ❌ | ❌ |

**Toucan's moat:** Free, offline-first, AI-powered analysis, open-source, extensible. The gap vs cloud platforms (Crowdin/Phrase) is team collaboration — that's v1.1+.
