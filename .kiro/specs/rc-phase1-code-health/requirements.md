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

1. WHEN the Build_System compiles the solution with `/warnaserror`, THE Build_System SHALL produce zero warnings across all projects
2. WHEN a new source file is added to the solution, THE Build_System SHALL enforce the same analysis level (`latest-all`) on that file
3. IF the Build_System detects an analyzer warning that cannot be resolved without behavioral change, THEN THE Build_System SHALL document the suppression with a justification comment

### Requirement 2: Nullable Annotation Enablement

**User Story:** As a developer, I want nullable annotations fully enabled in the WPF project, so that null-safety is enforced at compile time and null-reference bugs are prevented.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL have `<Nullable>enable</Nullable>` set in its project file
2. WHEN the Build_System compiles with nullable annotations enabled, THE Build_System SHALL produce zero CS8600–CS8605 nullable warnings
3. WHEN a public API accepts a nullable parameter, THE Toucan_WPF_Project SHALL annotate that parameter with the `?` suffix
4. WHEN a public API returns a value that may be null, THE Toucan_WPF_Project SHALL annotate the return type with the `?` suffix
5. IF a null-forgiving operator (`!`) is used, THEN THE source file SHALL contain a comment justifying why the suppression is safe

### Requirement 3: Newtonsoft.Json Removal

**User Story:** As a developer, I want all JSON handling consolidated to System.Text.Json, so that the project has fewer dependencies and uses the framework-native serializer.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Newtonsoft.Json`
2. WHEN a source file performs JSON serialization, THE JSON_Serializer SHALL use `System.Text.Json` APIs exclusively
3. WHEN a source file performs JSON deserialization, THE JSON_Serializer SHALL use `System.Text.Json` APIs exclusively
4. WHEN a project manifest is read and then written back without modification, THE JSON_Serializer SHALL produce output equivalent to the original input (round-trip preservation)
5. WHEN provider settings are serialized and then deserialized, THE JSON_Serializer SHALL produce an object equal to the original (round-trip preservation)
6. WHEN recent project data is serialized and then deserialized, THE JSON_Serializer SHALL produce an object equal to the original (round-trip preservation)

### Requirement 4: NuGet Dependency Reduction

**User Story:** As a developer, I want unnecessary NuGet packages removed and remaining packages pinned to exact versions, so that the dependency surface is minimal and reproducible.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL contain at most 8 PackageReference entries
2. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Avalonia` or `Avalonia.Desktop`
3. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `System.Data.DataSetExtensions`
4. THE Toucan_WPF_Project SHALL NOT contain a PackageReference to `Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers`
5. WHEN a PackageReference specifies a version, THE Toucan_WPF_Project SHALL use an exact version (no floating ranges or wildcards)
6. WHEN `dotnet list package --vulnerable` is executed, THE Build_System SHALL report zero known vulnerabilities

### Requirement 5: MainWindowViewModel Decomposition

**User Story:** As a developer, I want the monolithic MainWindowViewModel split into partial classes by concern, so that each file is focused, navigable, and maintainable.

#### Acceptance Criteria

1. THE MainWindowViewModel SHALL be declared as a `partial class` distributed across exactly five files: core, file operations, edit operations, translation operations, and navigation/filtering
2. WHEN a method belongs to file operations (open, save, close, import, export, recent projects), THE Partial_Class_File `MainWindowViewModel.File.cs` SHALL contain that method
3. WHEN a method belongs to edit operations (add, rename, delete, undo, redo, text transforms), THE Partial_Class_File `MainWindowViewModel.Edit.cs` SHALL contain that method
4. WHEN a method belongs to translation operations (pre-translate, providers, validation, analysis, bulk actions), THE Partial_Class_File `MainWindowViewModel.Translation.cs` SHALL contain that method
5. WHEN a method belongs to navigation and filtering (search, filter, pagination, tree navigation, view mode), THE Partial_Class_File `MainWindowViewModel.Nav.cs` SHALL contain that method
6. THE core file `MainWindowViewModel.cs` SHALL contain all field declarations, observable properties, the constructor, and shared infrastructure methods
7. WHEN CommunityToolkit.Mvvm source generators process the partial class, THE Build_System SHALL correctly generate all relay commands and observable property implementations across all partial files

### Requirement 6: Interface Consolidation in Core

**User Story:** As a developer, I want all service interfaces defined in Toucan.Core.Contracts, so that there is a single source of truth for contracts and no duplication between projects.

#### Acceptance Criteria

1. WHEN a WPF service interface defines a platform-agnostic contract (IDialogService, IMessageService, IPreferenceService, IProviderSettingsService, ISecureStorageService), THE Toucan_Core SHALL contain that interface definition
2. WHEN an interface is moved to Toucan_Core, THE Toucan_WPF_Project SHALL NOT retain a duplicate interface definition
3. WHEN a WPF service implements a Core contract, THE Toucan_WPF_Project SHALL reference the interface from Toucan_Core

### Requirement 7: Static Service Locator Elimination

**User Story:** As a developer, I want all static `App.Services` lookups replaced with constructor injection, so that dependencies are explicit, testable, and free of hidden coupling.

#### Acceptance Criteria

1. THE Toucan_WPF_Project SHALL NOT contain references to `App.Services` in any file except `App.xaml.cs`
2. WHEN a View or ViewModel requires a service, THE DI_Container SHALL provide that service through constructor injection
3. WHEN the application starts, THE DI_Container SHALL resolve the MainWindow and its full dependency graph without static property access
4. THE `App.xaml.cs` SHALL store the `IServiceProvider` as a private field rather than a public static property

### Requirement 8: DI Lifetime Correctness

**User Story:** As a developer, I want all service lifetimes correctly configured, so that no captive dependency or unintended sharing occurs at runtime.

#### Acceptance Criteria

1. WHEN a Singleton service is registered, THE DI_Container SHALL verify that none of its dependencies are registered as Transient
2. WHEN `ValidateOnBuild` is enabled on the service provider, THE DI_Container SHALL detect all lifetime violations at application startup
3. WHEN a service is registered, THE DI_Container SHALL use the lifetime that matches the service's actual usage pattern (Singleton for shared state, Transient for per-use instances)

### Requirement 9: Dependency Vulnerability and Version Documentation

**User Story:** As a developer, I want all package versions pinned and vulnerabilities documented, so that the release build is reproducible and free of known security issues.

#### Acceptance Criteria

1. WHEN the solution is built in CI, THE Build_System SHALL use deterministic package versions (no version ranges resolve differently over time)
2. THE solution SHALL document the minimum .NET SDK version required to build (currently .NET 10)
3. WHEN a vulnerability scan is performed, THE Build_System SHALL report the results and require zero high-severity vulnerabilities for release

### Requirement 10: Behavioral Preservation

**User Story:** As a user, I want all existing functionality to work identically after code health changes, so that the refactoring does not introduce regressions.

#### Acceptance Criteria

1. WHEN the Build_System compiles the solution after any refactoring step, THE Build_System SHALL succeed with zero errors
2. WHEN the existing test suite is executed after any refactoring step, THE Build_System SHALL report all tests passing with no regressions
3. WHEN a project file is opened, edited, saved, and reopened, THE Toucan_WPF_Project SHALL produce identical data to the pre-refactoring behavior
4. WHEN JSON data that was previously serialized with Newtonsoft.Json is loaded after migration, THE JSON_Serializer SHALL deserialize it correctly (backward compatibility)
