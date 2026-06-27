# Implementation Plan: RC Phase 1 — Code Health

## Overview

This plan implements the full code health pass for Toucan's release-candidate phase. Work is ordered to minimize merge conflicts and maximize incremental validation: dependency cleanup first (safe removals), then static analysis fixes, then architectural refactoring (ViewModel split, interface consolidation, DI migration). Each step must leave the build green and all tests passing.

## Tasks

- [x] 1. NuGet dependency cleanup and version pinning
  - [x] 1.1 Remove unused packages from Toucan.csproj
    - Remove PackageReferences: `Avalonia`, `Avalonia.Desktop`, `System.Data.DataSetExtensions`, `Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers`, `xunit.v3`
    - Verify remaining packages are at most 8 (target: 6)
    - Pin all remaining PackageReference entries to exact versions (no wildcards, no ranges)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 1.2 Create global.json at repository root
    - Specify minimum .NET SDK version (10.x)
    - Set `rollForward` policy to `latestPatch` to prevent automatic major-version upgrades
    - _Requirements: 9.2_

  - [x] 1.3 Run vulnerability scan and resolve findings
    - Execute `dotnet list package --vulnerable` against the solution
    - Resolve any High or Critical severity vulnerabilities by upgrading or replacing packages
    - Verify zero vulnerabilities remain
    - _Requirements: 4.6, 9.1, 9.3, 9.4_

- [x] 2. Checkpoint — Build and test after dependency cleanup
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Newtonsoft.Json removal and System.Text.Json migration
  - [x] 3.1 Migrate RecentProjectServices.cs to System.Text.Json
    - Replace `JsonConvert.SerializeObject` / `JsonConvert.DeserializeObject` with `JsonSerializer.Serialize` / `JsonSerializer.Deserialize<T>`
    - Configure `JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true }`
    - Ensure backward compatibility with existing JSON files on disk
    - _Requirements: 3.1, 3.2, 3.3, 3.6, 3.7_

  - [x] 3.2 Migrate ProviderSettingsService.cs to System.Text.Json
    - Replace Newtonsoft serialization/deserialization calls
    - Configure case-insensitive deserialization for backward compatibility
    - Verify provider settings round-trip (Provider, Options, Secrets properties preserved)
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.7_

  - [x] 3.3 Migrate OptionsViewModel.cs dynamic JSON access to System.Text.Json.Nodes
    - Replace `JObject.Parse` with `JsonNode.Parse(...).AsObject()`
    - Replace `JObject` / `JArray` manipulation with `JsonObject` / `JsonArray`
    - Use `root["key"]?.GetValue<string>()` instead of `root["key"]?.ToString()`
    - Preserve project manifest structure, ordering, and unmodified properties on write-back
    - _Requirements: 3.1, 3.4, 3.8_

  - [x] 3.4 Migrate MainWindowViewModel.cs JSON usage to System.Text.Json.Nodes
    - Replace `JObject.Parse` at line ~1191 with `JsonNode.Parse`
    - Update any related JSON property access patterns
    - _Requirements: 3.1, 3.8_

  - [x] 3.5 Remove Newtonsoft.Json PackageReference from Toucan.csproj
    - Remove the `<PackageReference Include="Newtonsoft.Json" .../>` entry
    - Remove any remaining `using Newtonsoft.Json` directives across all source files
    - Verify build succeeds with zero Newtonsoft references
    - _Requirements: 3.1_

  - [ ]* 3.6 Write property tests for JSON round-trip correctness
    - **Property 1: JSON serialization round-trip** — For any valid ProviderSettings object, serializing with System.Text.Json and deserializing SHALL produce an equal object
    - **Property 2: Backward-compatible JSON deserialization** — For any valid JSON string previously produced by Newtonsoft.Json, deserializing with System.Text.Json SHALL produce a correctly populated object
    - **Validates: Requirements 3.4, 3.5, 3.6, 3.7, 10.4**

  - [ ]* 3.7 Write unit tests for project manifest read-write round-trip
    - **Property 3: Project manifest read-write round-trip** — Reading and writing back without modification SHALL preserve structure and values
    - Test edge cases: empty objects, null values, nested arrays, special characters
    - **Validates: Requirements 3.4, 10.3**

- [x] 4. Checkpoint — Build and test after JSON migration
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Analyzer warning resolution and nullable enablement
  - [x] 5.1 Configure Directory.Build.props for strict analysis
    - Set `<AnalysisLevel>latest-all</AnalysisLevel>` in Directory.Build.props
    - Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` or equivalent `/warnaserror` enforcement
    - Ensure all projects in the solution inherit these settings
    - _Requirements: 1.1, 1.2, 1.4_

  - [x] 5.2 Resolve all analyzer warnings across the solution
    - Build with `/warnaserror` and fix all warnings in both Debug and Release configurations
    - For warnings that cannot be resolved without behavior change, add `#pragma warning disable` or `[SuppressMessage]` with rule ID and justification comment
    - Verify zero warnings remain across all projects including test projects
    - _Requirements: 1.1, 1.3_

  - [x] 5.3 Enable nullable annotations in Toucan.csproj
    - Change `<Nullable>warnings</Nullable>` (or add) to `<Nullable>enable</Nullable>`
    - Fix all CS8600–CS8605 nullable warnings
    - Annotate public APIs with `?` suffix where parameters accept null or return types may be null
    - Add justification comments for any null-forgiving operator (`!`) usage
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 6. Checkpoint — Build and test after static analysis cleanup
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Interface consolidation in Toucan.Core
  - [x] 7.1 Move service interfaces to Toucan.Core.Contracts namespace
    - Move `IDialogService`, `IMessageService`, `IPreferenceService`, `IProviderSettingsService`, `ISecureStorageService` from `Toucan/Services/` to `Toucan.Core/Contracts/`
    - Declare as `public interface` in `Toucan.Core.Contracts` namespace
    - Remove duplicate interface definitions from the WPF project
    - _Requirements: 6.1, 6.2_

  - [x] 7.2 Update WPF service implementations to reference Core contracts
    - Update all service classes in `Toucan/Services/` to use `using Toucan.Core.Contracts;`
    - Ensure project reference from Toucan → Toucan.Core exists
    - Verify solution compiles across all projects (Toucan.Core, Toucan WPF, Toucan.Core.Tests)
    - _Requirements: 6.3, 6.4_

- [x] 8. MainWindowViewModel decomposition into partial classes
  - [x] 8.1 Create MainWindowViewModel.File.cs partial class
    - Create file with `internal partial class MainWindowViewModel` declaration
    - Move file operation methods: OpenProject, LoadFolderAsync, SaveProject, SaveProjectAs, CloseProject, NewProject, ImportProject, ExportExcel, RefreshRecentProjects, and related commands/helpers
    - Verify build succeeds after move
    - _Requirements: 5.1, 5.2_

  - [x] 8.2 Create MainWindowViewModel.Edit.cs partial class
    - Create file with `internal partial class MainWindowViewModel` declaration
    - Move edit operation methods: AddLanguage, RenameItem, DeleteItem, CreateNewItem, Undo, Redo, text transforms (Convert*, Trim*, Simplify*), clipboard operations, ApplyToAllValues
    - Verify build succeeds after move
    - _Requirements: 5.1, 5.3_

  - [x] 8.3 Create MainWindowViewModel.Translation.cs partial class
    - Create file with `internal partial class MainWindowViewModel` declaration
    - Move translation methods: PreTranslate flow, provider options, validation, analysis, bulk actions, translation memory/suggestions
    - Verify build succeeds after move
    - _Requirements: 5.1, 5.4_

  - [x] 8.4 Create MainWindowViewModel.Nav.cs partial class
    - Create file with `internal partial class MainWindowViewModel` declaration
    - Move navigation/filtering methods: Search, ShowAll, FilterAndDisplay, pagination, OnSearchTextChanged, OnSelectedNodeChanged, ViewMode toggling, filter history
    - Verify build succeeds after move
    - _Requirements: 5.1, 5.5_

  - [x] 8.5 Finalize core MainWindowViewModel.cs
    - Retain all field declarations, [ObservableProperty] members, constructor, and shared utility methods (PagedUpdates, UpdatePageButtons, OpenUrl, RefreshTree, UpdateSummaryInfo)
    - Ensure methods referenced by two or more partial files remain in core
    - Verify all [RelayCommand] source generation works correctly across partial files
    - Verify XAML bindings still resolve to generated ICommand properties
    - _Requirements: 5.6, 5.7, 5.8_

- [x] 9. Checkpoint — Build and test after ViewModel decomposition
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Static service locator elimination and DI migration
  - [x] 10.1 Register all Views and ViewModels in DI container
    - Register `MainWindow`, `OptionsDialog`, `ProviderSettingsWindow`, and all other Views as Transient in `ConfigureServices`
    - Register ViewModels: `StatusBarViewModel` as Singleton, all others as Transient
    - Register all Core services as Singleton
    - Register factory delegates (`Func<TParam, TViewModel>`) for ViewModels requiring runtime parameters
    - _Requirements: 7.2, 8.3, 8.4, 8.5_

  - [x] 10.2 Refactor View code-behinds to use constructor injection
    - For each View with `App.Services?.GetService()` calls: add constructor parameters for required services, store in readonly fields, replace static lookups
    - Remove all fallback instantiation patterns (`?? new T()`)
    - Verify each View is resolved correctly through DI
    - _Requirements: 7.1, 7.2, 7.5_

  - [x] 10.3 Remove static App.Services property
    - Change `App.xaml.cs` to store `IServiceProvider` as a private field (`_services`)
    - Resolve `MainWindow` from DI in `Application_Startup`
    - Remove the `public static IServiceProvider Services` property
    - Verify zero remaining `App.Services` references in production code (except `App.xaml.cs` private field)
    - _Requirements: 7.1, 7.3, 7.4_

  - [x] 10.4 Enable DI lifetime validation
    - Add `ValidateOnBuild = true` and `ValidateScopes = true` to `ServiceProviderOptions`
    - Verify application starts without lifetime validation exceptions
    - Ensure DI failure throws an exception with a message indicating the unresolved service type
    - _Requirements: 8.1, 8.2, 8.6, 7.6_

  - [ ]* 10.5 Write unit tests for DI container configuration
    - Test that all services resolve correctly from the container
    - Test that ValidateOnBuild catches captive dependency violations (Singleton → Transient)
    - Test that startup throws on missing registrations
    - _Requirements: 8.1, 8.2, 8.6_

- [x] 11. Final verification and behavioral preservation
  - [x] 11.1 Full solution build verification
    - Build entire solution in both Debug and Release with `/warnaserror`
    - Verify zero errors and zero warnings
    - _Requirements: 1.1, 10.1_

  - [x] 11.2 Run complete test suite
    - Execute all tests in Toucan.Core.Tests and Toucan.Tests
    - Verify all tests pass with no regressions
    - _Requirements: 10.2_

  - [ ]* 11.3 Write integration test for project file round-trip
    - Open a project file, make edits, save, reopen, and verify data integrity
    - Verify JSON backward compatibility with Newtonsoft-serialized data
    - _Requirements: 10.3, 10.4, 10.5_

- [x] 12. Final checkpoint — All tests pass, code health targets met
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major phase
- Property tests validate universal correctness properties from the design document
- The ordering (dependency cleanup → JSON migration → analyzers → architecture) minimizes conflicts
- Always verify the build after each partial class move to catch source generator issues early
- System.Text.Json is already available as a framework reference — no new package needed

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "3.4"] },
    { "id": 3, "tasks": ["3.5"] },
    { "id": 4, "tasks": ["3.6", "3.7"] },
    { "id": 5, "tasks": ["5.1"] },
    { "id": 6, "tasks": ["5.2", "5.3"] },
    { "id": 7, "tasks": ["7.1"] },
    { "id": 8, "tasks": ["7.2"] },
    { "id": 9, "tasks": ["8.1", "8.2", "8.3", "8.4"] },
    { "id": 10, "tasks": ["8.5"] },
    { "id": 11, "tasks": ["10.1"] },
    { "id": 12, "tasks": ["10.2", "10.3"] },
    { "id": 13, "tasks": ["10.4"] },
    { "id": 14, "tasks": ["10.5"] },
    { "id": 15, "tasks": ["11.1", "11.2"] },
    { "id": 16, "tasks": ["11.3"] }
  ]
}
```
