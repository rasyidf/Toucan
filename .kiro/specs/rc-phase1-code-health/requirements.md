# Requirements Document

## Introduction

This document specifies the requirements for RC Phase 1 — Code Health of the Toucan application. The goal is to stabilize the existing codebase for release-candidate quality by eliminating analyzer warnings, removing unnecessary dependencies, decomposing monolithic components, centralizing contracts, and replacing service locator patterns with proper dependency injection. No new features are introduced — only restructuring, cleanup, and hardening of existing functionality.

## Glossary

- **Toucan_WPF_Project**: The main WPF application project (`Toucan.csproj`) containing the desktop UI layer
- **Toucan_Core**: The shared library project (`Toucan.Core.csproj`) containing platform-agnostic business logic and contracts
- **Build_System**: The .NET SDK build tooling (`dotnet build`) including analyzers and source generators
- **DI_Container**: The Microsoft.Extensions.DependencyInjection service provider used for constructor injection
- **MainWindowViewModel**: The primary view model class orchestrating the application's main window behavior
- **Partial_Class_File**: A C# source file containing a portion of a `partial class` definition, enabling logical separation of concerns
- **Static_Service_Locator**: The anti-pattern of accessing services via a static `App.Services` property rather than constructor injection
- **Analyzer_Warning**: A diagnostic message produced by Roslyn analyzers during compilation indicating potential code quality issues
- **Nullable_Annotation**: C# 8+ syntax indicating whether a reference type can hold null (`?` suffix)
- **PackageReference**: A NuGet dependency declaration in a `.csproj` file specifying package name and version
- **JSON_Serializer**: The System.Text.Json library used for serialization and deserialization of data objects
- **Round_Trip**: The property that data serialized and then deserialized (or vice versa) produces an equivalent result

## Requirements

### Requirement 1: Analyzer Warning Resolution

**User Story:** As a developer, I want all analyzer warnings resolved to zero, so that the codebase has a clean baseline and new issues are immediately visible.

#### Acceptance Criteria

1. WHEN the Build_System compiles the solution in both Debug and Release configurations with `/warnaserror`, THE Build_System SHALL produce zero warnings across all projects in the solution including test projects
2. WHEN a new source file is added to any project in the solution, THE Build_System SHALL apply the `latest-all` analysis level to that file without requiring per-project configuration, enforced via Directory.Build.props so that no individual project can lower the analysis level
3. IF the Build_System detects an analyzer warning that cannot be resolved without altering the runtime behavior of existing functionality, THEN THE Build_System SHALL suppress the warning using a `#pragma warning disable` directive or `[SuppressMessage]` attribute accompanied by an inline comment stating the analyzer rule ID and the reason the suppression is necessary
4. IF a project in the solution does not inherit the `AnalysisLevel` property from Directory.Build.props, THEN the Build_System SHALL fail the build indicating the misconfigured project

### Requirement 2: Nullable Annotation Enablement

**User Story:** As a developer, I want nullable annotations fully enabled in the WPF project, so that null-safety is enforced at compile time and null-reference bugs are prevented.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL have `<Nullable>enable</Nullable>` set in its project file
2. WHEN the Build_System compiles with nullable annotations enabled, THE Build_System SHALL produce zero CS8600–CS8605 nullable warnings
3. WHEN a public API accepts a nullable parameter, THE Toucan_WPF_Project SHALL annotate that parameter with the `?` suffix
4. WHEN a public API returns a value that may be null, THE Toucan_WPF_Project SHALL annotate the return type with the `?` suffix
5. IF a null-forgiving operator (`!`) is used, THEN THE source file SHALL contain a comment on the same or preceding line justifying why the suppression is safe

### Requirement 3: Newtonsoft.Json Removal

**User Story:** As a developer, I want all JSON handling consolidated to System.Text.Json, so that the project has fewer dependencies and uses the framework-native serializer.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Newtonsoft.Json`
2. WHEN a source file performs JSON serialization, THE JSON_Serializer SHALL use `System.Text.Json` APIs exclusively
3. WHEN a source file performs JSON deserialization, THE JSON_Serializer SHALL use `System.Text.Json` APIs exclusively
4. WHEN a project manifest is read and then written back with only targeted property changes, THE JSON_Serializer SHALL preserve all unmodified properties, property ordering, and nesting structure of the original file
5. WHEN provider settings are serialized and then deserialized, THE JSON_Serializer SHALL produce an object whose Provider, Options, and Secrets properties contain the same keys and values as the original object
6. WHEN recent project data is serialized and then deserialized, THE JSON_Serializer SHALL produce objects whose Path and LastOpened properties match the original values (LastOpened within 1-second precision)
7. IF a JSON file on disk was previously written by Newtonsoft.Json (using camelCase or PascalCase property names with indented formatting), THEN THE JSON_Serializer SHALL deserialize it without error and without data loss
8. WHEN a source file performs dynamic JSON access (reading or writing individual properties by name), THE JSON_Serializer SHALL use `System.Text.Json.Nodes` APIs (JsonNode, JsonObject, JsonArray) exclusively

### Requirement 4: NuGet Dependency Reduction

**User Story:** As a developer, I want unnecessary NuGet packages removed and remaining packages pinned to exact versions, so that the dependency surface is minimal and reproducible.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL contain at most 8 PackageReference entries
2. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Avalonia` or `Avalonia.Desktop`
3. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `System.Data.DataSetExtensions`
4. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers`
5. WHEN a PackageReference specifies a version, THE Toucan_WPF_Project SHALL use an exact version string (e.g., `8.4.2`) with no wildcards (`*`), floating versions, or version ranges (`[1.0,2.0)`)
6. WHEN `dotnet list package --vulnerable` is executed against the solution, THE Build_System SHALL report zero known vulnerabilities at any severity level

### Requirement 5: MainWindowViewModel Decomposition

**User Story:** As a developer, I want the monolithic MainWindowViewModel split into partial classes by concern, so that each file is focused, navigable, and maintainable.

#### Acceptance Criteria

1. THE MainWindowViewModel SHALL be declared as a `partial class` distributed across exactly five files: `MainWindowViewModel.cs` (core), `MainWindowViewModel.File.cs` (file operations), `MainWindowViewModel.Edit.cs` (edit operations), `MainWindowViewModel.Translation.cs` (translation operations), and `MainWindowViewModel.Nav.cs` (navigation/filtering)
2. WHEN a method belongs to file operations (open, save, close, import, export, recent projects, refresh, reveal in explorer), THE Partial_Class_File `MainWindowViewModel.File.cs` SHALL contain that method and its directly-related private helpers
3. WHEN a method belongs to edit operations (add, rename, delete, duplicate, undo, redo, clipboard cut/copy/paste, text transforms), THE Partial_Class_File `MainWindowViewModel.Edit.cs` SHALL contain that method and its directly-related private helpers
4. WHEN a method belongs to translation operations (pre-translate, providers, validation, analysis, bulk actions, statistics, source code scanning), THE Partial_Class_File `MainWindowViewModel.Translation.cs` SHALL contain that method and its directly-related private helpers
5. WHEN a method belongs to navigation and filtering (search, filter, pagination, tree navigation, view mode toggle, show untranslated/translated/approved, clear filter), THE Partial_Class_File `MainWindowViewModel.Nav.cs` SHALL contain that method and its directly-related private helpers
6. THE core file `MainWindowViewModel.cs` SHALL contain all field declarations, observable properties, the constructor, and utility methods referenced by two or more partial files (such as `RefreshTree`, `UpdateSummaryInfo`, `OpenUrl`)
7. WHEN CommunityToolkit.Mvvm source generators process the partial class, THE Build_System SHALL compile with zero errors and all [RelayCommand]-attributed methods SHALL have their generated ICommand properties accessible from XAML bindings
8. WHEN a method's categorization is ambiguous, THE Partial_Class_File SHALL assign it based on the method's primary domain action rather than its trigger context, and each method SHALL exist in exactly one partial file

### Requirement 6: Interface Consolidation in Core

**User Story:** As a developer, I want all service interfaces defined in Toucan.Core.Contracts, so that there is a single source of truth for contracts and no duplication between projects.

#### Acceptance Criteria

1. WHEN a WPF service interface defines a platform-agnostic contract (IDialogService, IMessageService, IPreferenceService, IProviderSettingsService, ISecureStorageService), THE Toucan_Core SHALL contain that interface definition as a public interface in the Toucan.Core.Contracts namespace
2. WHEN an interface is moved to Toucan_Core, THE Toucan_WPF_Project SHALL NOT retain an interface with the same name or equivalent contract in the Toucan.Services namespace
3. WHEN a WPF service class implements a Core contract, THE Toucan_WPF_Project SHALL reference the interface from Toucan.Core via a project reference and use the Toucan.Core.Contracts namespace in its using directives
4. WHEN all 5 interfaces (IDialogService, IMessageService, IPreferenceService, IProviderSettingsService, ISecureStorageService) have been consolidated, THE solution SHALL compile without errors across Toucan.Core, Toucan (WPF), and Toucan.Core.Tests projects

### Requirement 7: Static Service Locator Elimination

**User Story:** As a developer, I want all static `App.Services` lookups replaced with constructor injection, so that dependencies are explicit, testable, and free of hidden coupling.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL NOT contain compile-time invocations or accesses of `App.Services` in any production source file (`.cs`) except `App.xaml.cs`, excluding test projects and code comments
2. WHEN a View or ViewModel requires a service, THE DI_Container SHALL provide that service through constructor injection, including DI-registered factory delegates (`Func<TParam, TViewModel>`) for types requiring runtime parameters
3. WHEN the application starts, THE DI_Container SHALL resolve the MainWindow and its MainWindowViewModel dependency graph without any static property access to `App.Services`
4. THE `App.xaml.cs` SHALL store the `IServiceProvider` as a private field rather than a public static property
5. THE Toucan_WPF_Project SHALL NOT contain fallback instantiation patterns (e.g., `?? new T()`) that silently mask DI resolution failures in any production source file except `App.xaml.cs`
6. IF the DI_Container fails to resolve a required dependency during application startup, THEN THE application SHALL throw an exception with a message indicating the unresolved service type rather than launching with null dependencies

### Requirement 8: DI Lifetime Correctness

**User Story:** As a developer, I want all service lifetimes correctly configured, so that no captive dependency or unintended sharing occurs at runtime.

#### Acceptance Criteria

1. WHEN the DI_Container builds the service provider, THE DI_Container SHALL enable `ValidateOnBuild = true` and `ValidateScopes = true` on the ServiceProviderOptions
2. WHEN `ValidateOnBuild` is enabled and a Singleton service declares a constructor dependency on a Transient service, THE DI_Container SHALL throw an exception during application startup before the main window is shown
3. THE DI_Container SHALL register StatusBarViewModel as Singleton
4. THE DI_Container SHALL register all other ViewModels (MainWindowViewModel, NewProjectViewModel, LanguagePromptViewModel, OptionsViewModel, PreTranslateViewModel, ProviderSettingsViewModel, StartScreenViewModel, TranslationItemViewModel) as Transient
5. THE DI_Container SHALL register all Core services (IFileService, IProjectService, IPretranslationService, IBulkActionService, IProviderSettingsService, ISecureStorageService, IPreferenceService, IDialogService, IMessageService, IRecentProjectService) as Singleton
6. WHEN the application starts with all services registered, THE DI_Container SHALL resolve the full dependency graph without throwing any lifetime validation exceptions

### Requirement 9: Dependency Vulnerability and Version Documentation

**User Story:** As a developer, I want all package versions pinned and vulnerabilities documented, so that the release build is reproducible and free of known security issues.

#### Acceptance Criteria

1. WHEN the solution is built, THE Build_System SHALL resolve all PackageReference entries using exact version numbers only (no wildcards, floating versions, or version ranges such as `*`, `1.x`, or `[1.0,2.0)`)
2. THE solution SHALL specify the minimum required .NET SDK version in a `global.json` file at the repository root, with the `rollForward` policy set to prevent automatic major-version upgrades
3. WHEN `dotnet list package --vulnerable` is executed against the solution, THE Build_System SHALL produce zero vulnerabilities at High or Critical severity level
4. IF `dotnet list package --vulnerable` reports one or more High or Critical severity vulnerabilities, THEN THE Build_System SHALL fail the release build

### Requirement 10: Behavioral Preservation

**User Story:** As a user, I want all existing functionality to work identically after code health changes, so that the refactoring does not introduce regressions.

#### Acceptance Criteria

1. WHEN the Build_System compiles the solution after any refactoring step, THE Build_System SHALL succeed with zero errors
2. WHEN the existing test suite is executed after any refactoring step, THE Build_System SHALL report all tests passing with no regressions
3. WHEN a project file is opened, edited, saved, and reopened, THE Toucan_WPF_Project SHALL produce identical data to the pre-refactoring behavior
4. WHEN JSON data that was previously serialized with Newtonsoft.Json is loaded after migration, THE JSON_Serializer SHALL deserialize it correctly without data loss (backward compatibility)
5. WHEN the DI_Container resolves the full application dependency graph at startup, THE application SHALL present the same user-visible behavior as the pre-refactoring version
