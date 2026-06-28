# Requirements Document

## Introduction

This document defines requirements for a unified and robust Project and Translation IO layer in Toucan — a WPF/C# translation management application. The scope covers the full project lifecycle (New, Open, Save, Save As, Close), translation change tracking with dirty-state management, comment persistence, audit metadata, configurable save strategies (auto-save and on-demand), language management with disk cleanup, and an external file-change detection system with diff/merge capabilities.

## Glossary

- **Toucan_App**: The WPF desktop application that manages translation projects.
- **Project_Lifecycle_Service**: The service responsible for coordinating New, Open, Save, Save As, and Close operations on translation projects.
- **Translation_Change_Tracker**: The component that monitors in-memory translation edits and maintains dirty state for individual items and the project as a whole.
- **Save_Engine**: The component responsible for persisting translation data to disk using the configured strategy (ISaveStrategy implementations).
- **Auto_Save_Service**: The service that periodically persists unsaved changes based on a configurable interval.
- **File_Watcher_Service**: The service that monitors the project folder for external file changes and coordinates reload or merge actions.
- **Diff_Merge_Engine**: The component that computes differences between in-memory state and on-disk state, presenting conflicts for user resolution.
- **Language_Manager**: The component responsible for adding and removing languages, including disk artifact cleanup.
- **Audit_Service**: The component that records change metadata (timestamps, change type) on translation items.
- **ITranslationManagementService**: New service to be introduced — encapsulates translation CRUD, per-item dirty tracking, debounce coordination, and dirty-item queries. Replaces inline logic in TranslationItemViewModel and MainWindowViewModel.Translation.cs.
- **ILanguageManagementService**: New service to be introduced — encapsulates language add/remove (including disk cleanup), language reordering, and manifest language-list updates. Replaces inline logic in MainWindowViewModel.Edit.cs and LanguageManagerViewModel.
- **IProjectService**: Existing core service (ProjectService) responsible for Load/Save operations. To be extended with explicit lifecycle methods (OpenProject, CloseProject, SaveProject, SaveProjectAs).
- **IFileWatcherService**: New interface for the existing FileWatcherService — enables DI registration and testability.
- **IValidationPipeline**: Existing validation service that runs pre-save validation rules against translation data.
- **UndoRedoService**: Existing singleton service managing undo/redo stacks for translation edits (max 200 actions). Currently uses static Instance; to be moved into DI.
- **Project_Manifest**: The `toucan.project` JSON file that stores project configuration, language list, and editor preferences.
- **Translation_Item**: A single translation entry identified by Language, Namespace, Value, Comment, and approval status (class: TranslationItem).
- **Dirty_Flag**: A boolean indicator that the in-memory state differs from the last persisted state.
- **DI_Container**: The Microsoft.Extensions.DependencyInjection ServiceCollection configured in App.xaml.cs.

## Requirements

### Requirement 1: Unified Project Open

**User Story:** As a translator, I want all project-opening paths (folder picker, .project file picker, recent projects, New Project dialog) to funnel through a single load pipeline, so that project initialization is consistent regardless of entry point.

#### Acceptance Criteria

1. WHEN a user selects a folder via the Open Folder dialog, THE Project_Lifecycle_Service SHALL pass the selected folder path to the unified load pipeline, which loads translations, parses the Project_Manifest, initializes the File_Watcher_Service on the project folder, adds the path to the Recent Projects list, and displays the translation workspace.
2. WHEN a user selects a `.project` file via the Open Project File dialog, THE Project_Lifecycle_Service SHALL resolve the parent directory of the selected file and pass that directory path to the unified load pipeline.
3. WHEN a user selects a recent project from the Recent Projects list, THE Project_Lifecycle_Service SHALL pass the stored folder path to the unified load pipeline.
4. WHEN a user completes the New Project dialog, THE Project_Lifecycle_Service SHALL create the project folder, write the Project_Manifest with the user-specified languages and save style, and pass the new folder path to the unified load pipeline.
5. IF the specified project folder does not exist or is inaccessible, THEN THE Project_Lifecycle_Service SHALL display an error message indicating the folder could not be found or accessed, remove the entry from the Recent Projects list if it was a recent-project invocation, and remain on the current screen with all prior in-memory state unchanged.
6. IF a project is already open when a new project load is initiated, THEN THE Project_Lifecycle_Service SHALL close the current project (following the Unsaved Changes Prompt defined in Requirement 3) before executing the unified load pipeline for the new project.
7. IF the Project_Manifest file exists in the target folder but fails schema validation or cannot be parsed, THEN THE Project_Lifecycle_Service SHALL display an error message indicating the manifest is invalid and remain on the current screen with all prior in-memory state unchanged.

### Requirement 2: Save and Save As

**User Story:** As a translator, I want explicit Save and Save As operations that persist all in-memory changes to disk, so that I control when changes are committed.

#### Acceptance Criteria

1. WHEN the user triggers the Save command, THE Save_Engine SHALL persist all in-memory Translation_Items to the project folder using the project's configured ISaveStrategy.
2. WHEN the Save operation completes successfully, THE Translation_Change_Tracker SHALL clear the Dirty_Flag for all persisted items and for the project.
3. WHEN the user triggers Save As and selects a new folder, THE Save_Engine SHALL persist all translations to the selected folder, update the current project path, write a new Project_Manifest in the target folder, and initialize the File_Watcher_Service on the new folder.
4. IF a Save operation fails due to a file system error, THEN THE Save_Engine SHALL display an error message indicating the failure reason, retain the Dirty_Flag so no data is silently lost, and leave all in-memory state unchanged.
5. WHEN the Save operation succeeds, THE File_Watcher_Service SHALL update its file timestamp snapshot to reflect the newly written files.
6. WHEN the Save command is triggered, THE Save_Engine SHALL invoke the IValidationPipeline before persisting. IF the pipeline returns errors with severity Error, THEN THE Save_Engine SHALL display the validation errors and prompt the user to confirm or cancel the save.

### Requirement 3: Unsaved Changes Prompt on Close

**User Story:** As a translator, I want to be warned before losing unsaved work when I close a project or exit the application, so that I do not accidentally discard edits.

#### Acceptance Criteria

1. WHEN the user triggers Close Project and the Dirty_Flag is set, THE Toucan_App SHALL display a modal confirmation dialog offering exactly three options: Save, Discard, and Cancel.
2. WHEN the user selects Save from the unsaved-changes dialog, THE Save_Engine SHALL persist changes before closing the project. IF the save operation fails, THEN THE Toucan_App SHALL display an error message indicating the failure reason, keep the project open, and preserve all unsaved edits.
3. WHEN the user selects Discard from the unsaved-changes dialog, THE Toucan_App SHALL close the project without saving and clear the Dirty_Flag.
4. WHEN the user selects Cancel from the unsaved-changes dialog, THE Toucan_App SHALL abort the close operation and return to the editor with the Dirty_Flag and all edits unchanged.
5. WHEN the user attempts to exit the application and the Dirty_Flag is set, THE Toucan_App SHALL display the same unsaved-changes dialog before terminating.
6. WHEN the user triggers Open Project, New Project, or Open Recent while the Dirty_Flag is set, THE Toucan_App SHALL display the unsaved-changes dialog before replacing the current project.

### Requirement 4: Translation Change Tracking

**User Story:** As a translator, I want per-item dirty tracking so the application knows exactly which translations have been modified since the last save, enabling efficient partial saves and accurate status indicators.

#### Acceptance Criteria

1. WHEN a user modifies a Translation_Item value in the editor, THE Translation_Change_Tracker SHALL mark that item as dirty after the debounce period (500ms) elapses.
2. WHEN any Translation_Item is marked dirty, THE Translation_Change_Tracker SHALL set the project-level Dirty_Flag. WHEN all previously dirty Translation_Items are reverted or saved such that no dirty items remain, THE Translation_Change_Tracker SHALL clear the project-level Dirty_Flag.
3. WHEN a Save operation persists a dirty Translation_Item, THE Translation_Change_Tracker SHALL clear that item's dirty mark and store the newly persisted value as the item's baseline ("last-saved value").
4. THE Translation_Change_Tracker SHALL expose a method returning all currently dirty Translation_Items for use by the Save_Engine and UI indicators.
5. WHEN the user performs an Undo that reverts a Translation_Item's value to its last-saved baseline, THE Translation_Change_Tracker SHALL clear the dirty mark for that item. WHEN the user performs a Redo that changes a Translation_Item's value away from its last-saved baseline, THE Translation_Change_Tracker SHALL mark that item as dirty.
6. IF a Save operation fails, THEN THE Translation_Change_Tracker SHALL retain all dirty marks and baselines unchanged.
7. WHEN a project is loaded, THE Translation_Change_Tracker SHALL initialize the baseline for each Translation_Item to its loaded value, with all items marked as clean.

### Requirement 5: Comment Persistence and Change Tracking

**User Story:** As a translator, I want comments on translations to be tracked and persisted reliably, so that reviewer notes survive across save/load cycles in all supported formats.

#### Acceptance Criteria

1. WHEN a user modifies a Comment on a Translation_Item, THE Translation_Change_Tracker SHALL mark that item as dirty after the debounce period (500ms) elapses.
2. WHEN the Save_Engine persists translations using a format that supports inline comments (Xliff, Resx, PO, AndroidXml), THE Save_Engine SHALL write Comment values inline within the translation file for all Translation_Items that have non-empty comments.
3. IF the configured save format does not support inline comments (JSON, Namespaced, YAML, TOML, IosStrings, Arb, Csv, INI, JavaProperties, LaravelPhp), THEN THE Save_Engine SHALL persist comments in a sidecar file named `<translation-filename>.comments.json` in the same directory as the translation file.
4. WHEN the project is loaded, THE Project_Lifecycle_Service SHALL restore Comment values from the persisted source (inline or sidecar) into the Translation_Items matched by Language and Namespace.
5. IF a sidecar comments file references a Namespace that does not exist in the corresponding translation file, THEN THE Project_Lifecycle_Service SHALL discard that orphaned comment entry and not load it into memory.
6. WHEN a Translation_Item's Comment is changed from a non-empty value to an empty string and subsequently saved, THE Save_Engine SHALL remove that item's comment entry from the persisted source (inline element or sidecar entry).
7. THE Save_Engine SHALL enforce a maximum Comment length of 2000 characters per Translation_Item, truncating any value exceeding this limit at save time.

### Requirement 6: Audit Metadata

**User Story:** As a project manager, I want an audit trail on translation changes so that I can track when items were modified and categorize changes as suggestions or approved edits.

#### Acceptance Criteria

1. WHEN a Translation_Item is successfully persisted by the Save_Engine, THE Audit_Service SHALL record a LastModified timestamp in UTC ISO 8601 format (second precision) on that item.
2. WHEN a Translation_Item is marked as approved, THE Audit_Service SHALL record the approval timestamp in UTC ISO 8601 format (second precision) on that item.
3. THE Audit_Service SHALL store a ChangeType field on each Translation_Item supporting values: Direct_Edit, Suggestion, and Change_Request. Newly created Translation_Items SHALL default to Direct_Edit.
4. WHEN a user edits a Translation_Item value directly in the editor, THE Audit_Service SHALL set the ChangeType to Direct_Edit. WHEN a Translation_Item value is populated by the pretranslation engine, THE Audit_Service SHALL set the ChangeType to Suggestion.
5. WHEN the project is saved, THE Save_Engine SHALL persist audit metadata (LastModified timestamp, approval timestamp, ChangeType) in a dedicated sidecar metadata file alongside the translation files.
6. WHEN the project is loaded, THE Project_Lifecycle_Service SHALL restore audit metadata from the sidecar metadata file into the corresponding Translation_Items.
7. IF the audit metadata file is missing or contains malformed data on project load, THEN THE Project_Lifecycle_Service SHALL load the project without audit metadata, assigning default values (no timestamps, ChangeType of Direct_Edit) to affected items, and SHALL display a warning message indicating that audit history is unavailable.

### Requirement 7: Auto-Save Strategy

**User Story:** As a translator, I want an optional auto-save feature so that my work is periodically persisted without manual intervention, reducing risk of data loss.

#### Acceptance Criteria

1. WHERE the user enables auto-save in project settings, THE Auto_Save_Service SHALL persist all translation data at the configured interval when IsDirty is true.
2. THE Auto_Save_Service SHALL use a default interval of 60 seconds when no custom interval is specified, and SHALL constrain any user-configured interval to the range of 10 to 600 seconds inclusive.
3. WHILE auto-save is enabled, THE Auto_Save_Service SHALL skip a save cycle if IsDirty is false.
4. WHILE auto-save is enabled, THE Auto_Save_Service SHALL update the File_Watcher_Service snapshot by calling TakeSnapshot after each successful auto-save to prevent false external-change detections.
5. WHERE the user disables auto-save in project settings, THE Auto_Save_Service SHALL stop the periodic timer and revert to on-demand save only.
6. WHEN a manual save completes while auto-save is enabled, THE Auto_Save_Service SHALL reset the auto-save timer interval to start a new full cycle from zero.
7. IF an auto-save operation fails due to a file system error, THEN THE Auto_Save_Service SHALL retain the IsDirty state unchanged, display a non-blocking notification indicating the save failure reason, and retry on the next scheduled cycle.
8. WHILE an auto-save write operation is in progress, THE Auto_Save_Service SHALL not initiate another auto-save cycle until the current operation completes or fails.

### Requirement 8: Language Removal with Disk Cleanup

**User Story:** As a project manager, I want removing a language to also delete its files and folders from disk, so that the project folder stays clean and consistent with the in-memory state.

#### Acceptance Criteria

1. IF the user attempts to remove the primary language via the Language_Manager, THEN THE Language_Manager SHALL prevent the removal and display an error message indicating that the primary language cannot be removed.
2. WHEN the user removes a non-primary language via the Language_Manager, THE Language_Manager SHALL display a confirmation dialog listing the specific file paths and folder paths that will be deleted from disk.
3. WHEN the user confirms language removal, THE Language_Manager SHALL remove all Translation_Items for that language from memory.
4. WHEN the user confirms language removal, THE Language_Manager SHALL delete all translation files and empty language-specific folders for that language from the project folder on disk, continuing on a best-effort basis if individual file deletions fail.
5. WHEN the user confirms language removal, THE Language_Manager SHALL remove the language entry from the Project_Manifest languages list and persist the updated manifest.
6. IF one or more language files cannot be deleted due to a file system error, THEN THE Language_Manager SHALL complete the in-memory removal and manifest update, and display an error message listing each file path that could not be removed.
7. WHEN the user cancels the language removal confirmation dialog, THE Language_Manager SHALL abort the operation and leave all Translation_Items, disk files, and the Project_Manifest unchanged.

### Requirement 9: External File Change Detection

**User Story:** As a translator working in a team, I want the application to detect when translation files are modified externally (e.g., by a colleague's commit or a script), so that I am aware of changes and can decide how to incorporate them.

#### Acceptance Criteria

1. WHILE a project is loaded, THE File_Watcher_Service SHALL monitor the project folder recursively for file creation, modification, and deletion events, excluding build artifact files (.obj, .bin).
2. WHILE a project is loaded, THE File_Watcher_Service SHALL debounce file system events by waiting 2 seconds of inactivity before evaluating changes, to prevent redundant notifications from rapid successive writes.
3. WHEN file system changes are detected after the debounce period, IF the project Dirty_Flag is not set, THEN THE File_Watcher_Service SHALL automatically reload the affected translation files and update the file timestamp snapshot.
4. WHEN file system changes are detected after the debounce period, IF the project Dirty_Flag is set, THEN THE File_Watcher_Service SHALL display a notification offering Reload, Merge, or Ignore options.
5. WHEN the user selects Reload, THE Project_Lifecycle_Service SHALL reload all translations from disk, discard in-memory changes, clear the Dirty_Flag, and update the File_Watcher_Service file timestamp snapshot.
6. WHEN the user selects Ignore, THE Toucan_App SHALL dismiss the notification and retain the current in-memory state without updating the file timestamp snapshot.
7. WHEN the File_Watcher_Service detects file changes that match its own most recent snapshot update (i.e., changes caused by a Save or Auto-Save operation), THE File_Watcher_Service SHALL suppress the change event and not notify the user.

### Requirement 10: Diff and Merge for Conflicting Changes

**User Story:** As a translator, I want a diff view when external changes conflict with my unsaved edits, so that I can selectively keep my changes or accept the external updates.

#### Acceptance Criteria

1. WHEN the user selects Merge from the external-change notification, THE Diff_Merge_Engine SHALL compute a three-way per-item diff using the last-saved snapshot as base, the current in-memory state as "mine," and the newly loaded disk state as "theirs," matching items by their composite key (Language, Namespace).
2. WHEN the diff is computed, THE Diff_Merge_Engine SHALL categorize each difference as: Added_On_Disk (present in theirs, absent in base), Modified_On_Disk (changed in theirs vs base, unchanged in mine), Deleted_On_Disk (absent in theirs, present in base, unchanged in mine), or Conflicting (changed in both mine and theirs relative to base).
3. WHEN a Conflicting item is identified, THE Diff_Merge_Engine SHALL present both versions (in-memory value and on-disk value) side by side in a conflict-resolution dialog, allowing the user to choose one version per item.
4. WHEN the user resolves all conflicts and confirms, THE Diff_Merge_Engine SHALL apply the resolved values to the in-memory state and mark all resolved Conflicting items as dirty for the next save.
5. WHEN the merge is initiated, THE Diff_Merge_Engine SHALL apply non-conflicting changes automatically: adding Added_On_Disk items to memory, updating Modified_On_Disk items in memory, and removing Deleted_On_Disk items from memory, without requiring user input.
6. IF the user cancels the conflict-resolution dialog before resolving all conflicts, THEN THE Diff_Merge_Engine SHALL abort the merge, leave the in-memory state unchanged, and dismiss the dialog.

### Requirement 11: Project Manifest Consistency

**User Story:** As a developer, I want the Project_Manifest (toucan.project) to always reflect the current state of the project (languages, save style, paths), so that external tools and CI pipelines can rely on it as a source of truth.

#### Acceptance Criteria

1. WHEN a language is added to the project, THE Project_Lifecycle_Service SHALL append that language code to the Project_Manifest languages list and persist the manifest to disk within the same operation, without waiting for a separate save action.
2. WHEN a language is removed from the project, THE Project_Lifecycle_Service SHALL remove that language from the Project_Manifest languages list and persist the manifest to disk within the same operation, without waiting for a separate save action.
3. IF the language being removed is the current primaryLanguage, THEN THE Project_Lifecycle_Service SHALL assign the first remaining language in the languages list as the new primaryLanguage before persisting the manifest.
4. WHEN the project is saved, THE Save_Engine SHALL update the translationPackages entries in the Project_Manifest so that each TranslationPackage's translationUrls list contains one entry per language file with its current relative path and language code, then persist the manifest.
5. WHEN a new project is created without a toucan.project file, THE Project_Lifecycle_Service SHALL generate a default Project_Manifest containing: the folder name as projectName, the SaveStyle determined by the framework detector, the languages detected from existing translation files, and the first detected language as primaryLanguage.
6. WHEN the Project_Manifest is written to disk, THE Project_Lifecycle_Service SHALL validate the written content against the toucan.project.schema.json schema and log a warning if validation fails, without preventing the write from completing.
7. IF a manifest write operation fails due to a file system error, THEN THE Project_Lifecycle_Service SHALL retain the updated manifest state in memory and notify the user with an error message indicating the file path and failure reason.

### Requirement 12: Service Decomposition from ViewModel

**User Story:** As a developer, I want translation management and language management logic extracted from the ViewModel partial classes into dedicated, DI-registered services, so that business logic is testable in isolation and ViewModels stay thin.

#### Acceptance Criteria

1. THE Toucan_App SHALL introduce an ITranslationManagementService (registered as singleton in DI) responsible for: maintaining the in-memory translation collection, per-item dirty tracking (an item is dirty when its Value property differs from the last-persisted value), debounced write coordination with a debounce interval of 300 milliseconds, and exposing a method to query all dirty items for the Save_Engine.
2. THE Toucan_App SHALL introduce an ILanguageManagementService (registered as singleton in DI) responsible for: adding a language (creating empty-value TranslationItem entries for every existing namespace and writing the corresponding language file to disk), removing a language (removing all TranslationItem entries for that language from memory, deleting the language file from disk, and removing the language entry from the toucan.project manifest), and reordering languages (updating the language order in the toucan.project manifest).
3. THE existing IProjectService SHALL be extended with explicit lifecycle methods (OpenProject, CloseProject, SaveProject, SaveProjectAs) where each method orchestrates its steps in the following order: validation, persistence, manifest update, and File_Watcher_Service snapshot refresh, returning a result indicating success or failure with associated validation errors.
4. THE MainWindowViewModel partial files (File.cs, Edit.cs, Translation.cs) SHALL delegate all data manipulation and persistence logic to ITranslationManagementService, ILanguageManagementService, and IProjectService, retaining only RelayCommand declarations, UI property bindings, and view-navigation logic — with no file I/O, collection mutation, or validation invocation remaining in ViewModel code.
5. THE UndoRedoService SHALL remain a singleton but be registered in the DI ServiceCollection (removing the static Lazy<UndoRedoService> Instance property) so that ITranslationManagementService can receive it via constructor injection.
6. THE File_Watcher_Service SHALL be refactored behind an IFileWatcherService interface exposing Watch, Stop, TakeSnapshot methods and a FilesChanged event, and registered as a singleton in DI, enabling constructor-injection into IProjectService and removing direct instantiation from the ViewModel.
7. IF IValidationPipeline returns one or more errors with ValidationSeverity.Error during the IProjectService SaveProject flow, THEN THE IProjectService SHALL abort persistence, return the validation errors to the caller, and leave the project state unchanged.
8. WHEN a new service is introduced, THE Toucan_App SHALL register it in the ServiceCollection in App.xaml.cs following the existing pattern (singleton for stateful services that hold project data or subscriptions, transient for stateless services with no shared state).
9. WHEN IProjectService.SaveProject completes successfully, THE IProjectService SHALL invoke IFileWatcherService.TakeSnapshot to update the baseline file timestamps before returning control to the caller.
