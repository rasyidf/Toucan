# Toucan v1.0 Release Candidate Plan

> Current state: v0.7.0 → targeting v1.0-rc1
> Features: 88/88 complete, ~95% BabelEdit parity
> Codebase: 249 C# files, 3 projects (Core: 102, WPF: 85, Avalonia: 57)
> Goal: **Stabilize, polish, ship.** No new features until RC passes.

---

## Guiding Principles

1. **No new features** — everything needed is built, now make it solid
2. **Fix what's broken** — resolve analyzer warnings, dead code, inconsistencies
3. **Measure what matters** — startup time, memory, test coverage
4. **Ship confidence** — users should trust the app won't lose data or crash

---

## RC Phase 1: Code Health (Week 1–2)

### Static Analysis Cleanup
- [ ] Resolve all 60+ analyzer warnings to zero
- [ ] Nullable annotations: enable `<Nullable>enable</Nullable>` in WPF project (match Core)
- [ ] Remove dead code paths and unused parameters
- [ ] Remove `Newtonsoft.Json` from WPF — consolidate to `System.Text.Json`
- [ ] Reduce NuGet dependencies (target: < 8 packages in WPF)

### Architecture Hygiene
- [ ] Split `MainWindowViewModel` (51KB, 1300+ lines) into partial classes by concern:
  - File operations
  - Edit operations
  - Translation operations
  - Navigation/filtering
- [ ] Move WPF `Services/` interfaces to Core contracts (eliminate duplication)
- [ ] Replace static `App.Services` lookups with constructor injection
- [ ] Audit DI registrations — ensure all services have correct lifetimes

### Dependency Audit
- [ ] Pin all NuGet package versions (no floating ranges)
- [ ] Check for known vulnerabilities (`dotnet list package --vulnerable`)
- [ ] Document minimum .NET version requirement

---

## RC Phase 2: Testing (Week 2–3)

### Unit Tests (target: 80% Core coverage)
- [ ] Load/Save round-trip tests for all 14 formats
- [ ] Translation provider mocking — verify parameter preservation
- [ ] Validation pipeline — test all 6 rules independently
- [ ] Translation memory — fuzzy match accuracy tests
- [ ] Source code scanner — pattern extraction tests
- [ ] Package service — multi-set operations
- [ ] Undo/redo — verify state consistency after complex sequences

### Integration Tests
- [ ] Full project lifecycle: create → edit → save → reload → verify
- [ ] File watcher: modify on disk → app reloads correctly
- [ ] Large file handling: 10K keys, verify no OOM or excessive load time
- [ ] Framework auto-detection: drop folder → correct profile selected

### Manual QA Checklist
- [ ] All keyboard shortcuts work as documented
- [ ] Dark/light theme switch — no visual artifacts
- [ ] RTL language editing — text direction, font rendering
- [ ] Excel round-trip — approved flags + comments preserved
- [ ] Plural forms — i18next and ICU variants handled

---

## RC Phase 3: Performance (Week 3–4)

### Profiling
- [ ] Measure startup time (target: < 1.5s cold start)
- [ ] Measure memory on 10K key project (target: < 200MB)
- [ ] Profile tree view rendering with deep namespaces (500+ nodes)
- [ ] Identify hot paths in validation pipeline

### Optimizations (only if profiling justifies)
- [ ] Debounce validation on rapid edits (300ms quiet period)
- [ ] Cache translation memory index (don't rescan on every query)
- [ ] Lazy-load tree children for deeply nested structures
- [ ] Virtualize list view for projects with 5K+ visible rows

---

## RC Phase 4: UX Polish (Week 4–5)

### Quality of Life
- [ ] Validation results as a dockable panel (not just save-time modal)
- [ ] Translation memory suggestions inline in edit field (subtle autocomplete)
- [ ] Better drag-drop feedback in tree (highlight valid drop targets)
- [ ] Package selector in toolbar (quick switch)
- [ ] Keyboard shortcut for analyze (Ctrl+Shift+A)

### Error Handling
- [ ] Graceful handling of corrupted project files (don't crash, show message)
- [ ] Network timeout handling for translation providers (retry + feedback)
- [ ] File permission errors on save (clear user-facing message)
- [ ] Unsaved changes guard on all exit paths (close, crash recovery)

### Documentation
- [ ] Provider setup guides in-app (per provider: where to get key, what to configure)
- [ ] Framework profile reference (what each expects, folder conventions)
- [ ] Keyboard shortcuts cheatsheet (exportable)

---

## RC Phase 5: Distribution (Week 5–6)

### MSIX Installer
- [ ] Create MSIX package with proper manifest
- [ ] Sign with code signing certificate
- [ ] File association: `.toucan.project` → Toucan
- [ ] Start menu + taskbar integration
- [ ] Uninstall cleanly (no registry residue)

### Release Prep
- [ ] CHANGELOG.md for v1.0 (consolidate all 0.x changes)
- [ ] Update README with install instructions + screenshots
- [ ] GitHub Release with MSIX artifact
- [ ] Landing page update (docs/index.html — update progress, add download link)

---

## Metrics & Exit Criteria

RC is ready to ship when all of these pass:

| Metric | Current | v1.0 Target | Blocker? |
|--------|---------|-------------|----------|
| Test coverage (Core) | ~15% | ≥ 80% | Yes |
| Analyzer warnings | 60+ | 0 | Yes |
| Build time | 12s | < 10s | No |
| Cold startup | ~3s | < 1.5s | No |
| Memory (10K keys) | unmeasured | < 200MB | Yes |
| NuGet deps (WPF) | 12 | ≤ 8 | No |
| Crash reports (dogfooding) | unknown | 0 for 1 week | Yes |
| All 14 format round-trips | untested | passing | Yes |

**Blockers** must pass before tagging v1.0. Non-blockers are best-effort.

---

## Technical Debt to Resolve Before 1.0

| Area | Problem | Resolution |
|------|---------|------------|
| MainWindowViewModel | 51KB monolith | Partial class split by concern |
| Duplicate interfaces | WPF services mirror Core contracts | Move to Core, delete duplicates |
| Newtonsoft.Json | Mixed with System.Text.Json | Remove, STJ only |
| Static DI access | `App.Services` in ViewModels | Constructor injection |
| Provider config | BuildProviderOptions() reaches into container | Inject resolved config |
| No streaming load | Full in-memory for all formats | Acceptable for v1.0 at < 200MB target |

---

## What v1.0 Does NOT Include

These are explicitly deferred. They go into the future roadmap:

- ❌ Avalonia cross-platform (stays WIP, separate track)
- ❌ Auto-updater (post-1.0, needs infrastructure)
- ❌ ConsistencyAI / AI-powered TM enhancements
- ❌ Real-time collaboration / Toucan Hub
- ❌ CLI tool
- ❌ CI/CD integration
- ❌ Plugin system
- ❌ Cloud/SaaS features
- ❌ Platform imports (Crowdin, Lokalise, Phrase)

---

## Timeline

```
Week 1-2: Code health + architecture cleanup
Week 2-3: Test coverage push (overlap with cleanup tail)
Week 3-4: Performance profiling + targeted fixes
Week 4-5: UX polish + error handling
Week 5-6: MSIX packaging + release prep
Week 6:   Tag v1.0-rc1, dogfood for 1 week
Week 7:   Fix RC issues → tag v1.0
```

Total: **~7 weeks** from start to stable release.
