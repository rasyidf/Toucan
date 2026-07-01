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
- **Feature**: Read `docs/roadmap.json` → find next unchecked items (3-5 per sprint)
- **Bugbash**: Dispatch parallel analysis agents → produce prioritized findings
- **Improvement**: Identify architectural issues from recent code or `docs/known-bugs.md`
- Present plan, get user confirmation

### 2. Implement
- For each item:
  - Read existing code first (understand before changing)
  - Follow ponytail rules (lazy senior dev, minimum working diff)
  - Proper MVVM: Model in Core, ViewModel in Toucan/ViewModels, View in Toucan/Views
  - Wire commands, menu items, keybindings (via KeybindingService)
  - Centralize shared logic (FileEnumerator, not per-file duplication)
  - Fix root cause, not symptoms — grep all callers

### 3. Build & Verify
- Run: `dotnet build --no-restore Toucan.Core\Toucan.Core.csproj`
- Run: `dotnet build --no-restore Toucan\Toucan.csproj`
- Must be 0 errors, 0 warnings before proceeding
- Fix any errors immediately
- Run tests if available: `dotnet test tests\Toucan.Core.Tests`
- **Important**: Kill running Toucan.exe first if build fails with file-lock errors

### 4. Update Known Bugs
- `docs/known-bugs.md`: Full inventory with status (Fixed/Open), file locations, descriptions
- `known-bugs.md` (root): Quick summary — active bugs + fixed-in-version lists
- Mark fixed items with version number

### 5. Update Roadmap (feature sprint only)
- In `docs/roadmap.json`: set `"done": true` for completed items
- Update progress counters

### 6. Bump Version
- Increment in both files:
  - `Toucan/Toucan.csproj` → `<AssemblyVersion>` + `<FileVersion>`
  - `Toucan/Properties/AssemblyInfo.cs` → `[assembly: AssemblyVersion]` + `[assembly: AssemblyFileVersion]`
- Version scheme: `0.MINOR.PATCH.0`
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
| Roadmap | `docs/roadmap.json` |
| Known bugs (full) | `docs/known-bugs.md` |
| Known bugs (summary) | `known-bugs.md` |
| Changelog | `CHANGELOG.md` |
| Version (csproj) | `Toucan/Toucan.csproj` → AssemblyVersion/FileVersion |
| Version (assembly) | `Toucan/Properties/AssemblyInfo.cs` |
| ViewModels | `Toucan/ViewModels/` |
| Views/XAML | `Toucan/Views/`, `Toucan/Views/Components/`, `Toucan/Views/Dialogs/` |
| Services | `Toucan/Services/` |
| Core models | `Toucan.Core/Models/` |
| Core services | `Toucan.Core/Services/` |
| Core contracts | `Toucan.Core/Contracts/`, `Toucan.Core/Contracts/Services/` |
| Load strategies | `Toucan.Core/Services/LoadStrategies/` |
| Save strategies | `Toucan.Core/Services/SaveStrategies/` |
| Translation providers | `Toucan.Core/Services/Providers/` |
| Validation rules | `Toucan.Core/Services/Validation/` |
| File enumeration | `Toucan.Core/Services/FileEnumerator.cs` |
| Options | `Toucan.Core/Options/AppOptions.cs` |
| Keybindings | `Toucan/Services/KeybindingService.cs` |
| Panel state | `Toucan/Services/PanelService.cs` |
| StatusBar | `Toucan/Services/StatusBarService.cs` |
| DI registration | `Toucan/App.xaml.cs` (line ~250+) |
| Design tokens | `Toucan/Resources/DesignTokens.xaml` |
| Menu | `Toucan/Views/Components/MainMenu.xaml` |
| Tests (Core) | `tests/Toucan.Core.Tests/` |
| Tests (WPF) | `tests/Toucan.Tests/` |

## Conventions

- All commands → `MainWindowViewModel.cs` (or partial files) as `[RelayCommand]`
- Use `CommunityToolkit.Mvvm` for ObservableProperty/RelayCommand
- New panels → register in PanelService
- New shortcuts → add to `KeybindingService.Apply()` AND `GetDefinitions()`
- New options → add to `AppOptions` with sensible defaults
- New formats → implement `ISaveStrategy` + `ILoadStrategy`, register in `App.xaml.cs` DI
- New providers → implement `ITranslationProvider`, register in `TranslationProviderRegistry`
- File crawling → use `FileEnumerator.EnumerateFiles()` with appropriate `EnumerateOptions`
- Thread safety → `ConcurrentBag`/`ConcurrentDictionary` for parallel ops, `Interlocked` for flags
- String matching → case-insensitive `StringComparer.OrdinalIgnoreCase` on Windows
- Namespace operations → always use exact match or prefix+dot (`ns == x || ns.StartsWith(x + ".")`)
- Tests → `tests/Toucan.Core.Tests/` for core logic, `tests/Toucan.Tests/` for VM tests

## Bug Analysis Pattern

When doing a bugbash, dispatch parallel agents across these areas:
1. **ViewModels** — null refs, async races, missing dispose, logic errors
2. **Core Services** — resource leaks, thread safety, incorrect logic, edge cases
3. **Providers & Strategies** — parsing errors, data loss, encoding, API misuse
4. **WPF UI** — thread violations, event leaks, broken bindings, missing feedback
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
