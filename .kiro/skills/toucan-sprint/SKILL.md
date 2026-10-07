---
title: Toucan Feature Sprint
inclusion: manual
---

# Toucan Feature Sprint

Automates the implement → build → document → version → commit → push cycle for Toucan development sessions.

## When to use

Activate when the user says: "implement next features", "continue roadmap", "next sprint", "implement and ship", "bugbash", "fix known bugs", "improvements", or similar.

## Session Modes

### Feature Sprint
Trigger: "next sprint", "implement features", "continue roadmap"
Focus: New capabilities from the roadmap. Implement → verify → document → bump minor.

### Bug Bash
Trigger: "bugbash", "fix bugs", "analyze and fix", "fix known bugs"
Focus: Find and fix bugs across the codebase. Analyze → prioritize → fix → verify → document.
1. Dispatch multiple analysis agents across areas (ViewModels, Services, Providers, UI, Models)
2. Categorize: Critical > High > Medium > Low
3. Fix in priority order, verify each builds
4. Document all findings in `docs/known-bugs.md`
5. Bump patch version

### Improvement Sprint
Trigger: "improvements", "refactor", "performance", "cleanup"
Focus: Non-functional improvements — performance, architecture, code quality.
1. Identify targets (duplicated code, O(n²), missing abstractions)
2. Extract/centralize shared utilities (e.g., `FileEnumerator`)
3. Verify no behavior change (build + existing tests pass)
4. Bump patch version

### Codefixing Session
Trigger: "codefixing session", "fix mode", "no commit", "don't commit"
Focus: Quick fixes without version/commit ceremony. Skip steps 4-8.

## Workflow

### 1. Plan
- **Feature**: Read `docs/todos/future-roadmap.md` → find the next feature groups (FG-xx) and unchecked items (2-4 groups per sprint)
- **Bugbash**: Dispatch parallel analysis agents → produce prioritized findings
- **Improvement**: Identify architectural issues from recent code or `docs/known-bugs.md`
- Present plan, get user confirmation

### 2. Implement
- For each item:
  - Read existing code first (understand before changing)
  - Follow ponytail rules (lazy senior dev, minimum working diff)
  - Proper MVVM: Model in Core, ViewModel in Toucan.Avalonia/ViewModels, View in Toucan.Avalonia/Views
  - Wire commands, menu items, keybindings (via KeybindingService)
  - Centralize shared logic (FileEnumerator, not per-file duplication)
  - Fix root cause, not symptoms — grep all callers

### 3. Build & Verify
- Run: `dotnet build Toucan.CrossPlatform.slnx`
- Must be 0 errors, 0 warnings before proceeding
- Fix any errors immediately
- Run tests: `dotnet test Toucan.CrossPlatform.slnx` (Core and Avalonia tests)
- **Important**: Quit a running Toucan app first if the build fails with file-lock errors

### 4. Update Known Bugs
- `docs/known-bugs.md` lists open issues only (ID, severity, repro, cause, fix direction)
- A fixed bug is deleted from it, with a `### Fixed` line in the changelog; never mark it "Fixed" in place

### 4b. Update doc headers
- Every doc has a YAML status header; follow `.agents/skills/doc-status/SKILL.md`.
- Update `status`, `progress`, `summary`, `updated` on each doc you touched; archive finished plans instead of deleting.
- Run `python3 .agents/skills/doc-status/scripts/docs_status.py check && python3 .agents/skills/doc-status/scripts/docs_status.py index`.

### 5. Move shipped work out of the roadmap (feature sprint only)
- Shipped items are **deleted** from `docs/todos/future-roadmap.md`, not ticked. A group with no open items is deleted with its sprint-table row.
- Add the user-visible change to `CHANGELOG.md` `[Unreleased]` (history) and, for a lasting capability, a line to `docs/completed-features.md` (shipped features only, no unchecked items)
- Update the roadmap header `progress` (open item count), `summary` and `updated`
- Update the README roadmap table and `docs/index.html` to match
- Run the doc-status `check`: it fails if a tracker still holds a ticked `- [x]` item

### 6. Bump Version
- Increment `<Version>` in `Directory.Build.props` (it applies to every project)
- Version scheme: `0.MINOR.PATCH`
  - Minor: significant new capabilities (new editor mode, new format, new panel)
  - Patch: bug fixes, improvements, refactors

### 7. Update Changelog
- `CHANGELOG.md` — add version section:
  ```markdown
  ## [0.X.Y] - YYYY-MM-DD
  
  ### Added (features only)
  - Feature description
  
  ### Fixed (bugs)
  - **Short title** — What was wrong and what the fix does.
  
  ### Improved (non-functional)
  - Improvement description
  ```

### 8. Commit & Push
- Create a branch: `fix/vX.Y.Z-description` or `feat/vX.Y.Z-description`
- Stage specific files (not `git add .`)
- Commit message format:
  ```
  fix: short summary (vX.Y.Z)
  
  - bullet points of key changes
  ```
- Push with tracking: `git push -u origin <branch>`
- Do NOT push directly to main

## File Locations

| What | Where |
|------|-------|
| Roadmap | `docs/todos/future-roadmap.md` |
| Doc index and status headers | `docs/INDEX.md`, skill `.agents/skills/doc-status` |
| Known bugs | `docs/known-bugs.md` |
| Changelog | `CHANGELOG.md` |
| Version | `Directory.Build.props` → `<Version>` |
| ViewModels | `Toucan.Avalonia/ViewModels/` |
| Views (AXAML) | `Toucan.Avalonia/Views/`, `Views/Components/`, `Views/Dialogs/`, `Views/Panels/` |
| Services | `Toucan.Avalonia/Services/` |
| Core models | `Toucan.Core/Models/` |
| Core services | `Toucan.Core/Services/` |
| Core contracts | `Toucan.Core/Contracts/`, `Toucan.Core/Contracts/Services/` |
| Load strategies | `Toucan.Core/Services/LoadStrategies/` |
| Save strategies | `Toucan.Core/Services/SaveStrategies/` |
| Translation providers | `Toucan.Core/Services/Providers/` |
| Validation rules | `Toucan.Core/Services/Validation/` |
| File enumeration | `Toucan.Core/Services/FileEnumerator.cs` |
| Options | `Toucan.Core/Options/AppOptions.cs` |
| Keybindings | `Toucan.Avalonia/Services/KeybindingService.cs` |
| Panel state | `Toucan.Avalonia/Services/PanelService.cs` |
| StatusBar | `Toucan.Avalonia/Services/StatusBarService.cs` |
| DI registration | `Toucan.Core/ToucanCoreServiceCollectionExtensions.cs` (`AddToucanCore()`), `Toucan.Avalonia/App.axaml.cs` |
| Styles | `Toucan.Avalonia/Styles/AppStyles.axaml` |
| Menu and command palette | `Toucan.Avalonia/Views/MainMenu.cs` |
| CLI | `Toucan.CLI/Program.cs` |
| Plugins | `Toucan.Core/Plugins/`, `Toucan.Plugins.Abstractions/` |
| Tests (Core) | `tests/Toucan.Core.Tests/` |
| Tests (Avalonia) | `tests/Toucan.Avalonia.Tests/` |

## Conventions

- All commands → `MainWindowViewModel.cs` (or its partial files in `Toucan.Avalonia/ViewModels/`) as `[RelayCommand]`
- Use `CommunityToolkit.Mvvm` for ObservableProperty/RelayCommand
- New panels → register in `PanelService`, add the case in `MainWindow.axaml.cs`
- New shortcuts → `Toucan.Avalonia/Services/KeybindingService.cs` (check how existing ones are defined)
- New options → add to `AppOptions` with sensible defaults
- New formats → implement `ISaveStrategy` + `ILoadStrategy`, register in `AddToucanFormats()` (see docs/ARCHITECTURE.md, "New Format")
- New providers → implement `ITranslationProvider` with a `Definition`, register in `AddToucanProviders()`
- File crawling → use `FileEnumerator.EnumerateFiles()` with appropriate `EnumerateOptions`
- Thread safety → `ConcurrentBag`/`ConcurrentDictionary` for parallel ops, `Interlocked` for flags
- String matching → case-insensitive `StringComparer.OrdinalIgnoreCase` on Windows
- Namespace operations → always use exact match or prefix+dot (`ns == x || ns.StartsWith(x + ".")`)
- Tests → `tests/Toucan.Core.Tests/` for core logic, `tests/Toucan.Avalonia.Tests/` for view models and headless UI

## Bug Analysis Pattern

When doing a bugbash, dispatch parallel agents across these areas:
1. **ViewModels** — null refs, async races, missing dispose, logic errors
2. **Core Services** — resource leaks, thread safety, incorrect logic, edge cases
3. **Providers & Strategies** — parsing errors, data loss, encoding, API misuse
4. **Avalonia UI** — thread violations, event leaks, broken bindings, missing feedback
5. **Models & Validation** — data integrity, tree operations, validation gaps

Severity levels:
- **Critical**: Data loss or corruption risk → fix immediately
- **High**: Incorrect behavior visible to user → fix in same session
- **Medium**: UX issues, resource leaks → fix if time permits
- **Low**: Performance, code quality → batch into improvement sprint

## Recent Architecture Decisions (v0.14.x)

- `FileEnumerator` is the single source of truth for directory exclusion across all loaders
- `EnumerateOptions.SkipNestedLocaleDirs` prevents duplicate loading from `locales/`+lang dirs
- `ITranslationManagementService.NotifyValueChanged()` is the official dirty-tracking channel
- `StatusBarService.GetViewModel()` exposes the VM for direct collection updates
- `MainWindow` receives `IFileWatcherService` via DI (not `new`)
- `HandleZenKeys` in `KeybindingService` guards Delete/F2/Escape when TextBox focused
- Provider fallback (no API key) → `Succeeded = false`, never fake translations
- YAML save uses `__self` convention for keys that are both parents and leaf values
