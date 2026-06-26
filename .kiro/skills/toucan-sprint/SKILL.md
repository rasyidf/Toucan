---
title: Toucan Feature Sprint
inclusion: manual
---

# Toucan Feature Sprint

Automates the implement → build → document → version → commit → push cycle for new Toucan features.

## When to use

Activate when the user says: "implement next features", "continue roadmap", "next sprint", "implement and ship", or similar.

## Workflow

### 1. Plan
- Read `docs/ROADMAP.md` — find next unchecked `- [ ]` items
- Present the plan (3-5 items per sprint based on effort)
- Get user confirmation or adjustments

### 2. Implement
- For each feature:
  - Create/modify the minimum files needed
  - Follow ponytail rules (lazy senior dev, minimum code)
  - Ensure proper MVVM: Model in Core, ViewModel in Toucan/ViewModels, View in Toucan/Views
  - Wire commands, menu items, keybindings (via KeybindingService)
  - Add to PanelService if it's a panel feature

### 3. Build & Verify
- Run: `dotnet build Toucan\Toucan.csproj`
- Must be 0 errors before proceeding
- Fix any errors immediately

### 4. Update Roadmap
- In `docs/ROADMAP.md`: change `- [ ]` to `- [x]` for completed items
- Update the `Done: X / Y (Z%)` progress line
- Update `Recommended Next Sprint` if completed items were listed there

### 5. Bump Version
- Increment patch (or minor for significant features) in:
  - `Toucan/Toucan.csproj` → `<AssemblyVersion>` + `<FileVersion>`
  - `Toucan/Properties/AssemblyInfo.cs` → `[assembly: AssemblyVersion]` + `[assembly: AssemblyFileVersion]`
- Version scheme: `0.MINOR.PATCH.0`
  - Patch: bug fixes, small features
  - Minor: significant new capabilities (new editor mode, new format, new panel)

### 6. Update Changelog
- In `CHANGELOG.md`, add new version section at top:
  ```markdown
  ## [X.Y.Z] — YYYY-MM-DD

  ### Added
  - **Feature name** — one-line description

  ### Changed
  - Description of changes

  ### Fixed
  - Bug fixes if any
  ```

### 7. Commit (Split)
Split into logical commits:
1. `feat: <main feature group>` — implementation files
2. `chore: bump version to X.Y.Z, update changelog and roadmap` — docs + version

### 8. Push
```
git push origin main
```

## File Locations

| What | Where |
|------|-------|
| Roadmap | `docs/ROADMAP.md` |
| Changelog | `CHANGELOG.md` |
| Version (csproj) | `Toucan/Toucan.csproj` lines AssemblyVersion/FileVersion |
| Version (assembly) | `Toucan/Properties/AssemblyInfo.cs` |
| ViewModels | `Toucan/ViewModels/` |
| Views/XAML | `Toucan/Views/` and `Toucan/Views/Components/` and `Toucan/Views/Dialogs/` |
| Services | `Toucan/Services/` |
| Core models | `Toucan.Core/Models/` |
| Core services | `Toucan.Core/Services/` |
| Options | `Toucan.Core/Options/AppOptions.cs` |
| Keybindings | `Toucan/Services/KeybindingService.cs` |
| Panel state | `Toucan/Services/PanelService.cs` |
| Design tokens | `Toucan/Resources/DesignTokens.xaml` |
| Menu | `Toucan/Views/Components/MainMenu.xaml` |

## Conventions

- All commands go in `MainWindowViewModel.cs` as `[RelayCommand]`
- Use `CommunityToolkit.Mvvm` for ObservableProperty/RelayCommand
- New panels → register in PanelService
- New shortcuts → add to KeybindingService.Apply() AND GetDefinitions()
- New options → add to `AppOptions` with sensible defaults
- New formats → implement ISaveStrategy + ILoadStrategy, register in App.xaml.cs DI
- Tests go in `tests/Toucan.Tests/` or `tests/Toucan.Core.Tests/`
