# Implementation Plan: Project and Translation IO

## Overview

This plan implements the unified IO layer for Toucan's project lifecycle and translation persistence. The work is structured as: foundational interfaces and DI refactors first, then core service implementations (dirty tracking, audit, comments), then lifecycle orchestration (open/save/close), then advanced features (auto-save, file watcher, diff/merge), and finally ViewModel delegation and wiring.

## Tasks

- [x] 1. Define core interfaces and data models
  - [x] 1.1 Create ITranslationManagementService interface and supporting types
    - Create file `Toucan.Core/Contracts/Services/ITranslationManagementService.cs`
    - Define the interface with all methods: Initialize, NotifyValueChanged, NotifyCommentChanged, GetDirtyItems, MarkAllSaved, MarkSaved, IsItemDirty, Clear, AddItems, RemoveItems
    - Define `IsDirty` property and `DirtyStateChanged` event
    - Create internal `TranslationBaseline` class in `Toucan.Core/Models/TranslationBaseline.cs`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 12.1_

  - [x] 1.2 Create IProjectLifecycleService interface and result types
    - Create file `Toucan.Core/Contracts/Services/IProjectLifecycleService.cs`
    - Define the interface with OpenProjectAsync, CreateAndOpenProjectAsync, SaveProjectAsync, SaveProjectAsAsync, CloseProjectAsync
    - Define enums and records: ProjectOpenStatus, ProjectOpenResult, ProjectSaveStatus, ProjectSaveResult, CloseResult, ProjectChangedEventArgs, ProjectChangeType
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.3, 3.1_

  - [x] 1.3 Create ILanguageManagementService interface and result types
    - Create file `Toucan.Core/Contracts/Services/ILanguageManagementService.cs`
    - Define the interface with AddLanguageAsync, RemoveLanguageAsync, GetLanguageFilePaths, ReorderLanguagesAsync
    - Define `LanguageOperationResult` record
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 12.2_

  - [x] 1.4 Create IAutoSaveService interface
    - Create file `Toucan.Core/Contracts/Services/IAutoSaveService.cs`
    - Define the interface with Start, Stop, ResetTimer, IsEnabled, AutoSaveFailed event
    - _Requirements: 7.1, 7.2, 7.5, 7.6_

  - [x] 1.5 Create IDiffMergeEngine interface and diff models
    - Create file `Toucan.Core/Contracts/Services/IDiffMergeEngine.cs`
    - Define the interface with ComputeDiff and ApplyNonConflicting methods
    - Define DiffEntry record, DiffCategory enum, DiffResult record, MergeResult record
    - _Requirements: 10.1, 10.2, 10.4, 10.5_

  - [x] 1.6 Create IAuditService interface and AuditMetadata model
    - Create file `Toucan.Core/Contracts/Services/IAuditService.cs`
    - Define the interface with RecordSave, RecordApproval, SetChangeType, GetMetadata, LoadFromSidecar, SaveToSidecar, Clear
    - Define ChangeType enum and AuditMetadata class
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

  - [x] 1.7 Create IFileWatcherService interface (refactor)
    - Create file `Toucan.Core/Contracts/Services/IFileWatcherService.cs`
    - Define the interface with Watch, Stop, TakeSnapshot methods and FilesChanged event
    - _Requirements: 9.1, 9.7, 12.6_

  - [x] 1.8 Create IUndoRedoService interface and refactor UndoRedoService for DI
    - Create file `Toucan.Core/Contracts/Services/IUndoRedoService.cs` with CanUndo, CanRedo, Record, Undo, Redo, Clear
    - Modify `Toucan/Services/UndoRedoService.cs` to implement `IUndoRedoService` and remove static `Lazy<UndoRedoService>` Instance property
    - _Requirements: 12.5_

  - [x] 1.9 Extend TranslationItem with audit metadata properties
    - Add `LastModifiedUtc`, `ApprovedAtUtc`, and `ChangeType` properties to `Toucan.Core/Models/TranslationItem.cs`
    - Add `AutoSaveEnabled` and `AutoSaveIntervalSeconds` properties to existing ProjectSettings / project model
    - _Requirements: 6.1, 6.2, 6.3, 7.1, 7.2_

- [x] 2. Implement ITranslationManagementService (dirty tracking and collection management)
  - [x] 2.1 Implement TranslationManagementService core logic
    - Create file `Toucan.Core/Services/TranslationManagementService.cs`
    - Implement Initialize: store baselines keyed by (Language, Namespace), mark all items clean
    - Implement NotifyValueChanged/NotifyCommentChanged with 500ms debounce using System.Threading.Timer
    - Implement GetDirtyItems: compare current Value/Comment against baseline
    - Implement IsDirty property as `GetDirtyItems().Count > 0`
    - Implement MarkAllSaved/MarkSaved: update baselines to current values
    - Implement Clear, AddItems, RemoveItems
    - Inject IUndoRedoService via constructor for undo-aware dirty detection
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1, 12.1_

  - [ ]* 2.2 Write property test: Dirty Tracking Consistency (Property 2)
    - **Property 2: Dirty Tracking Consistency**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.7, 5.1**
    - Create `tests/Toucan.Core.Tests/IO/TranslationManagementServiceTests.cs`
    - Use FsCheck to generate random sequences of value/comment edits, saves, undos, redos
    - Assert: item is dirty iff current Value != baseline Value OR current Comment != baseline Comment
    - Assert: IsDirty == (GetDirtyItems().Count > 0)

  - [ ]* 2.3 Write property test: Save Failure State Preservation (Property 3)
    - **Property 3: Save Failure State Preservation**
    - **Validates: Requirements 2.4, 4.6, 7.7**
    - Add test to `tests/Toucan.Core.Tests/IO/TranslationManagementServiceTests.cs`
    - Use FsCheck to generate dirty project states
    - Simulate save failure (exception), assert dirty items, values, and baselines unchanged

- [x] 3. Implement IAuditService
  - [x] 3.1 Implement AuditService with sidecar persistence
    - Create file `Toucan.Core/Services/AuditService.cs`
    - Implement RecordSave: set LastModifiedUtc to DateTime.UtcNow
    - Implement RecordApproval: set ApprovedAtUtc to DateTime.UtcNow
    - Implement SetChangeType: update ChangeType field
    - Implement LoadFromSidecar: read `.toucan-metadata.json`, parse JSON, map by (Language, Namespace)
    - Implement SaveToSidecar: serialize audit metadata to `.toucan-metadata.json`
    - Handle missing/malformed sidecar gracefully (defaults, log warning)
    - Implement Clear for project close
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [ ]* 3.2 Write property test: Audit Metadata Round-Trip (Property 7)
    - **Property 7: Audit Metadata Round-Trip**
    - **Validates: Requirements 6.5, 6.6**
    - Create `tests/Toucan.Core.Tests/IO/AuditServiceTests.cs`
    - Use FsCheck to generate TranslationItems with random audit metadata
    - SaveToSidecar then LoadFromSidecar in temp directory
    - Assert all metadata restored by (Language, Namespace) key

  - [ ]* 3.3 Write property test: Audit Timestamp Recording (Property 8)
    - **Property 8: Audit Timestamp Recording**
    - **Validates: Requirements 6.1, 6.2, 6.3**
    - Add test to `tests/Toucan.Core.Tests/IO/AuditServiceTests.cs`
    - Assert RecordSave sets non-null UTC LastModifiedUtc
    - Assert RecordApproval sets non-null UTC ApprovedAtUtc
    - Assert new items default to ChangeType.DirectEdit

- [x] 4. Implement comment persistence logic
  - [x] 4.1 Implement comment sidecar read/write in ProjectService
    - Extend save flow to detect format support for inline comments (Xliff, Resx, PO, AndroidXml)
    - For unsupported formats: write `<filename>.comments.json` sidecar with schema `{ schemaVersion, comments: { namespace: comment } }`
    - On load: restore comments from inline or sidecar, matched by (Language, Namespace)
    - Discard orphaned sidecar entries (namespace not in translation file)
    - Enforce 2000-char max on comment at save time (truncate)
    - Remove entries for empty comments
    - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.6, 5.7_

  - [ ]* 4.2 Write property test: Comment Persistence Round-Trip (Property 4)
    - **Property 4: Comment Persistence Round-Trip**
    - **Validates: Requirements 5.2, 5.3, 5.4**
    - Create `tests/Toucan.Core.Tests/IO/CommentPersistenceTests.cs`
    - Use FsCheck to generate items with non-empty comments (≤ 2000 chars)
    - Save and reload in temp directory, assert comments restored by (Language, Namespace)

  - [ ]* 4.3 Write property test: Empty Comment Removal (Property 5)
    - **Property 5: Empty Comment Removal**
    - **Validates: Requirements 5.6**
    - Add test to `tests/Toucan.Core.Tests/IO/CommentPersistenceTests.cs`
    - Generate items with empty comments, save, assert no entry in sidecar/inline for those keys

  - [ ]* 4.4 Write property test: Comment Length Truncation (Property 6)
    - **Property 6: Comment Length Truncation**
    - **Validates: Requirements 5.7**
    - Add test to `tests/Toucan.Core.Tests/IO/CommentPersistenceTests.cs`
    - Generate comments > 2000 chars, save, reload, assert length == 2000 and first 2000 chars preserved

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement ILanguageManagementService
  - [x] 6.1 Implement LanguageManagementService
    - Create file `Toucan.Core/Services/LanguageManagementService.cs`
    - Inject ITranslationManagementService, IProjectService, IFileWatcherService
    - Implement AddLanguageAsync: create empty-value TranslationItems for all existing namespaces, write language file to disk, update manifest languages list
    - Implement RemoveLanguageAsync: reject if primary language, remove items from memory, delete files from disk (best-effort), update manifest, persist manifest
    - Implement GetLanguageFilePaths: return all file paths for a language
    - Implement ReorderLanguagesAsync: update manifest language order
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 11.1, 11.2, 11.3, 12.2_

  - [ ]* 6.2 Write property test: Primary Language Protection (Property 10)
    - **Property 10: Primary Language Protection**
    - **Validates: Requirements 8.1**
    - Create `tests/Toucan.Core.Tests/IO/LanguageManagementServiceTests.cs`
    - Use FsCheck to generate projects with various primary languages
    - Assert RemoveLanguageAsync returns failure for primary language and items remain

  - [ ]* 6.3 Write property test: Language Removal Completeness (Property 11)
    - **Property 11: Language Removal Completeness**
    - **Validates: Requirements 8.3**
    - Add test to `tests/Toucan.Core.Tests/IO/LanguageManagementServiceTests.cs`
    - Generate project with multiple languages, remove non-primary
    - Assert zero in-memory items for removed language

  - [ ]* 6.4 Write property test: Manifest Language List Consistency (Property 12)
    - **Property 12: Manifest Language List Consistency**
    - **Validates: Requirements 8.5, 11.1, 11.2, 11.3**
    - Create `tests/Toucan.Core.Tests/IO/ManifestConsistencyTests.cs`
    - Generate sequences of add/remove operations
    - Assert manifest languages array matches in-memory language set
    - Assert primaryLanguage fallback to first remaining language if removed

- [x] 7. Implement IDiffMergeEngine
  - [x] 7.1 Implement DiffMergeEngine three-way diff logic
    - Create file `Toucan.Core/Services/DiffMergeEngine.cs`
    - Implement ComputeDiff: match by (Language, Namespace) composite key
    - Categorize: AddedOnDisk, ModifiedOnDisk, DeletedOnDisk, Conflicting per design spec
    - Implement ApplyNonConflicting: add/update/remove non-conflicting items in ITranslationManagementService
    - Mark resolved conflicting items as dirty
    - _Requirements: 10.1, 10.2, 10.4, 10.5, 10.6_

  - [ ]* 7.2 Write property test: Three-Way Diff Categorization (Property 13)
    - **Property 13: Three-Way Diff Categorization**
    - **Validates: Requirements 10.1, 10.2**
    - Create `tests/Toucan.Core.Tests/IO/DiffMergeEngineTests.cs`
    - Use FsCheck to generate (base, mine, theirs) item triples with controlled overlap
    - Assert categorization rules per spec for each DiffCategory

  - [ ]* 7.3 Write property test: Non-Conflicting Merge Correctness (Property 14)
    - **Property 14: Non-Conflicting Merge Correctness**
    - **Validates: Requirements 10.5**
    - Add test to `tests/Toucan.Core.Tests/IO/DiffMergeEngineTests.cs`
    - Generate DiffResults with only non-conflicting entries
    - Apply merge, assert AddedOnDisk items present, ModifiedOnDisk values updated, DeletedOnDisk items absent

- [x] 8. Implement IFileWatcherService refactor
  - [x] 8.1 Refactor FileWatcherService behind IFileWatcherService interface
    - Modify `Toucan/Services/FileWatcherService.cs` to implement `IFileWatcherService`
    - Implement Watch: start FileSystemWatcher on folder recursively, exclude .obj/.bin
    - Implement Stop: dispose FileSystemWatcher
    - Implement TakeSnapshot: record current file timestamps as baseline
    - Implement 2-second debounce on FilesChanged event
    - Suppress events matching own snapshot (saves/auto-saves don't trigger notifications)
    - _Requirements: 9.1, 9.2, 9.7, 12.6_

  - [ ]* 8.2 Write unit tests for FileWatcherService
    - Create `tests/Toucan.Core.Tests/IO/FileWatcherServiceTests.cs`
    - Test Watch/Stop lifecycle
    - Test TakeSnapshot suppresses self-triggered events
    - Test debounce consolidates rapid changes
    - _Requirements: 9.1, 9.2, 9.7_

- [x] 9. Implement IAutoSaveService
  - [x] 9.1 Implement AutoSaveService with timer logic
    - Create file `Toucan.Core/Services/AutoSaveService.cs`
    - Inject IProjectLifecycleService (via Lazy<> to break circular), ITranslationManagementService, IFileWatcherService
    - Implement Start: create System.Threading.Timer with clamped interval (10-600 seconds)
    - Implement Stop: dispose timer
    - Implement ResetTimer: reset interval on manual save
    - Skip save cycle if IsDirty is false
    - Call TakeSnapshot after successful auto-save
    - Handle save failure: retain dirty state, raise AutoSaveFailed event, retry next cycle
    - Guard against concurrent auto-save executions (SemaphoreSlim)
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_

  - [ ]* 9.2 Write property test: Auto-Save Interval Clamping (Property 9)
    - **Property 9: Auto-Save Interval Clamping**
    - **Validates: Requirements 7.2**
    - Create `tests/Toucan.Core.Tests/IO/AutoSaveServiceTests.cs`
    - Use FsCheck to generate arbitrary integer intervals
    - Assert effective interval == Math.Clamp(value, 10, 600)

  - [ ]* 9.3 Write unit tests for AutoSaveService behavior
    - Add tests to `tests/Toucan.Core.Tests/IO/AutoSaveServiceTests.cs`
    - Test: skips when not dirty
    - Test: resets timer after manual save
    - Test: fires AutoSaveFailed on error
    - Test: no concurrent executions
    - _Requirements: 7.3, 7.6, 7.7, 7.8_

- [x] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Implement IProjectLifecycleService (orchestration layer)
  - [x] 11.1 Implement ProjectLifecycleService unified open pipeline
    - Create file `Toucan.Core/Services/ProjectLifecycleService.cs`
    - Inject IProjectService, ITranslationManagementService, IFileWatcherService, IValidationPipeline, IAutoSaveService, IDiffMergeEngine, IAuditService, IDialogService, IRecentProjectService
    - Implement OpenProjectAsync: validate folder exists, parse manifest, load translations via IProjectService, initialize ITranslationManagementService, start IFileWatcherService, add to recent projects, load audit metadata
    - Handle unsaved-changes prompt if a project is already open (delegate to CloseProjectAsync)
    - Handle folder-not-found: show error, remove from recent if applicable
    - Handle manifest-invalid: show error, stay on current screen
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_

  - [x] 11.2 Implement ProjectLifecycleService save flows
    - Implement SaveProjectAsync: run IValidationPipeline, persist via IProjectService, update manifest translationPackages, call IAuditService.SaveToSidecar, call ITranslationManagementService.MarkAllSaved, call IFileWatcherService.TakeSnapshot, reset auto-save timer
    - Implement SaveProjectAsAsync: persist to new folder, update project path, write new manifest, init watcher on new folder
    - Handle validation errors: return errors for UI prompt, abort if cancelled
    - Handle file system errors: return failure, retain dirty state
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 11.4, 12.3, 12.7, 12.9_

  - [x] 11.3 Implement ProjectLifecycleService close and unsaved-changes prompt
    - Implement CloseProjectAsync: check IsDirty, show modal dialog (Save/Discard/Cancel)
    - Save path: call SaveProjectAsync, close on success, keep open on failure
    - Discard path: clear dirty state, stop file watcher, stop auto-save, clear services
    - Cancel path: return Cancelled, no state changes
    - Handle app exit scenario (same dialog)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 11.4 Implement CreateAndOpenProjectAsync for new project flow
    - Create project folder, write Project_Manifest with languages and save style
    - Pass new folder to unified load pipeline
    - Implement default manifest generation for folders without existing toucan.project
    - _Requirements: 1.4, 11.5, 11.6_

  - [ ]* 11.5 Write property test: Project File Path Resolution (Property 17)
    - **Property 17: Project File Path Resolution**
    - **Validates: Requirements 1.2**
    - Create `tests/Toucan.Core.Tests/IO/ProjectLifecycleServiceTests.cs`
    - Use FsCheck to generate valid file paths ending with `.project`
    - Assert resolved folder path == parent directory

  - [ ]* 11.6 Write property test: TranslationPackages Manifest Consistency (Property 15)
    - **Property 15: TranslationPackages Manifest Consistency**
    - **Validates: Requirements 11.4**
    - Add test to `tests/Toucan.Core.Tests/IO/ManifestConsistencyTests.cs`
    - After save, assert translationUrls contains one entry per language, each path exists on disk

  - [ ]* 11.7 Write property test: Default Manifest Generation (Property 16)
    - **Property 16: Default Manifest Generation**
    - **Validates: Requirements 11.5**
    - Add test to `tests/Toucan.Core.Tests/IO/ManifestConsistencyTests.cs`
    - Generate temp folders with translation files of known extensions
    - Assert generated manifest contains all detected languages
    - Assert primaryLanguage == first detected language

- [x] 12. Implement save round-trip and external change detection integration
  - [x] 12.1 Implement external file change handling in ProjectLifecycleService
    - Subscribe to IFileWatcherService.FilesChanged event
    - If not dirty: auto-reload affected files, update snapshot
    - If dirty: show notification with Reload/Merge/Ignore options
    - Reload: reload all from disk, clear dirty, update snapshot
    - Merge: invoke IDiffMergeEngine, present conflict dialog
    - Ignore: dismiss notification, retain in-memory state
    - _Requirements: 9.3, 9.4, 9.5, 9.6_

  - [ ]* 12.2 Write property test: Translation Save Round-Trip (Property 1)
    - **Property 1: Translation Save Round-Trip**
    - **Validates: Requirements 2.1**
    - Create `tests/Toucan.Core.Tests/IO/SaveRoundTripTests.cs`
    - Use FsCheck to generate collections of TranslationItems with arbitrary Language, Namespace, Value
    - Save via ISaveStrategy to temp folder, reload
    - Assert same set of (Language, Namespace, Value) tuples

- [x] 13. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Register all new services in DI and refactor ViewModels
  - [x] 14.1 Register new services in App.xaml.cs ServiceCollection
    - Register IUndoRedoService / UndoRedoService as singleton
    - Register IFileWatcherService / FileWatcherService as singleton
    - Register ITranslationManagementService / TranslationManagementService as singleton
    - Register IAuditService / AuditService as singleton
    - Register ILanguageManagementService / LanguageManagementService as singleton
    - Register IDiffMergeEngine / DiffMergeEngine as singleton
    - Register IAutoSaveService / AutoSaveService as singleton
    - Register IProjectLifecycleService / ProjectLifecycleService as singleton
    - Remove any static UndoRedoService.Instance usage
    - _Requirements: 12.5, 12.6, 12.8_

  - [x] 14.2 Refactor MainWindowViewModel.File.cs to delegate to IProjectLifecycleService
    - Inject IProjectLifecycleService into MainWindowViewModel
    - Replace inline open/save/close logic with calls to IProjectLifecycleService methods
    - Retain only RelayCommand declarations and UI bindings
    - Remove direct file I/O and collection mutation from ViewModel
    - _Requirements: 12.4_

  - [x] 14.3 Refactor MainWindowViewModel.Translation.cs to delegate to ITranslationManagementService
    - Inject ITranslationManagementService into MainWindowViewModel
    - Replace inline dirty tracking, value change handling with service calls
    - Subscribe to DirtyStateChanged for UI property updates
    - Remove per-item dirty logic from ViewModel
    - _Requirements: 12.4_

  - [x] 14.4 Refactor MainWindowViewModel.Edit.cs and LanguageManagerViewModel to delegate to ILanguageManagementService
    - Inject ILanguageManagementService into MainWindowViewModel and LanguageManagerViewModel
    - Replace inline language add/remove/reorder logic with service calls
    - Retain only command declarations and confirmation dialog triggers
    - _Requirements: 12.4_

  - [x] 14.5 Update all UndoRedoService consumers to use IUndoRedoService via DI
    - Replace all `UndoRedoService.Instance` references with injected `IUndoRedoService`
    - Update MainWindowViewModel.Edit.cs undo/redo commands to use injected service
    - _Requirements: 12.5_

- [x] 15. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 16. Add FsCheck test generators and remaining property tests
  - [x] 16.1 Create FsCheck generators for TranslationItem and project structures
    - Create `tests/Toucan.Core.Tests/IO/Generators/TranslationItemGenerator.cs`
    - Generate items with random Language (pool of 5-10 codes), Namespace (dot-separated 1-4 deep), Value (unicode 0-500 chars), Comment (0-3000 chars)
    - Create `tests/Toucan.Core.Tests/IO/Generators/EditSequenceGenerator.cs` for random edit/save/undo/redo sequences
    - Create `tests/Toucan.Core.Tests/IO/Generators/DiffTripleGenerator.cs` for (base, mine, theirs) item sets
    - Add FsCheck and FsCheck.Xunit NuGet packages to `Toucan.Core.Tests.csproj`
    - _Requirements: All property tests depend on these generators_

  - [x] 16.2 Add NSubstitute and System.IO.Abstractions to test projects
    - Add `NSubstitute` package to `Toucan.Core.Tests.csproj`
    - Add `System.IO.Abstractions` and `System.IO.Abstractions.TestingHelpers` packages
    - These are used for mocking interfaces and abstracting file system in tests
    - _Requirements: Test infrastructure_

- [x] 17. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1–17)
- Unit tests validate specific examples and edge cases
- All new services are singletons registered in DI (they hold project-scoped state)
- The existing IProjectService is extended rather than replaced
- FsCheck generators (task 16.1) should be created early if property tests are being implemented alongside core logic
- NSubstitute is used for mocking service interfaces in isolation tests

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9"] },
    { "id": 1, "tasks": ["16.1", "16.2", "2.1", "3.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "3.2", "3.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3", "4.4", "6.1", "7.1"] },
    { "id": 4, "tasks": ["6.2", "6.3", "6.4", "7.2", "7.3", "8.1"] },
    { "id": 5, "tasks": ["8.2", "9.1"] },
    { "id": 6, "tasks": ["9.2", "9.3", "11.1"] },
    { "id": 7, "tasks": ["11.2", "11.3", "11.4"] },
    { "id": 8, "tasks": ["11.5", "11.6", "11.7", "12.1"] },
    { "id": 9, "tasks": ["12.2"] },
    { "id": 10, "tasks": ["14.1"] },
    { "id": 11, "tasks": ["14.2", "14.3", "14.4", "14.5"] }
  ]
}
```
