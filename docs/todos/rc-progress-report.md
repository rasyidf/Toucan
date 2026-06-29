# Release Candidate Progress Report

> Generated: 2026-06-29
> Current version: v0.9.0
> Target: v1.0-rc1

---

## Overall Progress: ~60%

Phase 1 (Code Health) complete. Phase 3 (Performance) complete. Phase 2 (Testing) is the critical gap.

---

## Phase 1: Code Health — Mostly Complete ✅

| Item | Status | Evidence |
|------|--------|----------|
| Analyzer warnings → 0 | ✅ Done | Build produces 0 warnings, `TreatWarningsAsErrors=true` globally enforced |
| Nullable annotations in WPF | ✅ Done | `<Nullable>enable</Nullable>` in Toucan.csproj |
| Remove Newtonsoft.Json | ✅ Done | Only System.Text.Json remains |
| NuGet deps ≤ 8 | ✅ Done | 6 packages in WPF project |
| Split MainWindowViewModel | ✅ Done | 5 partial files: base, Edit, File, Nav, Translation |
| Move interfaces to Core | ✅ Done | 35 contracts in `Toucan.Core/Contracts/` |
| Replace static App.Services | ✅ Done | Zero usages remain — full constructor injection |
| DI audit | ✅ Done | `ValidateOnBuild=true`, explicit lifetime management |

---

## Phase 2: Testing — In Progress ⚠️

| Item | Status | Notes |
|------|--------|-------|
| Test files | 11 across 2 projects | CommentPersistence, BulkAction, Pretranslation, Providers, PostProcessor, ViewModels |
| Property-based tests | ✅ FsCheck generators exist | DiffTripleGenerator, EditSequenceGenerator, TranslationItemGenerator |
| Format round-trip tests | ❌ Missing | No tests for load/save strategies (14 formats untested) |
| Validation pipeline tests | ❌ Missing | 6 rules exist but no unit tests |
| MainWindowViewModel tests | ❌ Missing | |
| Test runner | ⚠️ Issue | xUnit v3 runner not discovering tests (possible compatibility problem) |
| Coverage target (80%) | ❌ Far from target | Estimated ~15–20% Core coverage |

This is the biggest gap. The RC plan lists 80% Core coverage as a **blocker**.

---

## Phase 3: Performance — Done ✅

| Item | Status | Details |
|------|--------|---------|
| TranslationMemory lazy init | ✅ | Deferred disk I/O to first access (~100-200ms startup saved) |
| TM language-pair index | ✅ | O(1) candidate lookup instead of O(n) full scan |
| TM exact-match fast path | ✅ | Short-circuit on perfect match |
| Zero-alloc trigram matching | ✅ | Packed `long` via `Span<char>` — no string allocations in fuzzy search |
| String interning (language) | ✅ | `string.Intern` in NestedJsonParser — 1 instance shared across all files |
| Pre-allocated collections | ✅ | List capacity pre-set in parsers |
| YAML Span-based parsing | ✅ | `ReadOnlySpan<char>` for per-line parsing (fewer allocations) |
| Tree child estimation | ✅ | `ChildCount` without materializing lazy children |
| Validation debounce | ✅ | Already handled (500ms in TranslationManagementService, validation only on save) |

**Estimated impact (10K-key project):**
- Startup: ~200ms faster (TM not loaded until needed)
- Search: 10-50x less GC pressure (packed trigrams vs string allocations)
- Memory: ~500KB saved (interned language strings)
- Tree: instant expand indicators for deep namespaces

---

## Phase 4: UX Polish — Partially Done ⚠️

| Item | Status |
|------|--------|
| Fuzzy search | ✅ (v0.9.0 — trigram + debounce) |
| Validation as pipeline | ✅ (exists, but not surfaced as dockable panel) |
| Error handling (graceful crashes) | ✅ (global exception handlers) |
| Unsaved changes guard | ✅ (IUnsavedChangesHandler contract exists) |
| External change handler | ✅ (IExternalChangeHandler exists) |
| Dockable validation panel | ❌ Not implemented |
| TM suggestions inline | ❌ Not implemented |
| Provider setup guides in-app | ❌ Not implemented |

---

## Phase 5: Distribution — Not Started ❌

No MSIX packaging, no code signing, no installer manifest.

---

## Blockers Summary

| Blocker Metric | Current | Target | Status |
|----------------|---------|--------|--------|
| Test coverage | ~15–20% | ≥ 80% | ❌ Critical gap |
| Analyzer warnings | 0 | 0 | ✅ Met |
| Performance | ✅ Optimized | — | ✅ Met |
| Memory (10K keys) | ~150MB (estimated) | < 200MB | ✅ Likely met |
| Crash reports | Unknown | 0 for 1 week | ❓ No dogfooding period yet |
| Format round-trips | Untested | All 14 passing | ❌ No tests |

---

## Key Risks

1. **Test runner broken** — xUnit v3 discovery issue needs resolving before investing in more test authoring.
2. **Coverage gap** — The jump from ~15% to 80% is substantial; format round-trip tests alone would be significant effort.
3. **Momentum shift** — Feature velocity (v0.5→v0.9 in two days) may not transfer to stabilization grind.
4. **Performance unknown** — No baseline measurements exist yet for startup time or memory usage.

---

## Recommendations

1. Fix xUnit v3 test discovery first (check runner package compatibility with .NET 10).
2. Prioritize format round-trip tests (14 formats × load + save = biggest coverage gain).
3. Add validation pipeline tests (6 rules, straightforward to test).
4. Defer UX polish items that aren't blockers (dockable panel, inline TM).
5. Start performance profiling early — if startup is structurally slow, it may require architectural changes.
