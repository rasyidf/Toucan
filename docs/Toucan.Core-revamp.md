# Toucan.Core Revamp — Design & Migration Plan

This document describes the changes introduced to `Toucan.Core` to support a standardized, modular translation strategy pipeline (SaveStrategy / LoadStrategy), a project type resolver (manifest or folder scan), migration steps and backward-compatibility considerations.

## Goals
- Add a symmetrical load and save strategy layer (ISaveStrategy + ILoadStrategy).
- Introduce `ProjectTypeVariant` to support two project modes: `ConfigManifest` (using `toucan.project`) and `FolderScan` (scan folder for files).
- Add `ITranslationStrategyFactory` and `IProjectModeResolver` to centralize selection and discovery.
- Introduce async-friendly file operations (IFileService async methods) while maintaining sync APIs for backwards compatibility.
- Keep existing `ProjectService` behaviour; migrate callers to new APIs gradually.

## Summary of Key Changes
- New interfaces:
  - `ILoadStrategy` — complementary to `ISaveStrategy`.
  - `ITranslationStrategyFactory` — resolves save/load strategies by `SaveStyles`.
  - `IProjectModeResolver` — resolves `ProjectTypeVariant` for folder-based vs manifest-based projects.
- New model:
  - `ProjectTypeVariant` enum — `FolderScan` or `ConfigManifest`.
- New services & implementations:
  - `JsonLoadStrategy` — loads JSON files from a folder.
  - `NamespacedLoadStrategy` — wraps `JsonLoadStrategy` for nested JSON.
  - `YamlLoadStrategy` — placeholder; future YAML load logic.
  - `TranslationStrategyFactory` — resolves registered strategies.
  - `ProjectModeResolver` — checks for `toucan.project` to decide project mode.
- `FileService` extended with async wrappers: `ReadAsync`, `SaveAsync`, etc.
- `ISaveStrategy` extended with `SaveAsync` wrapper; implementations added.
- `ProjectService` now accepts `ITranslationStrategyFactory` and `IProjectModeResolver` and uses `ILoadStrategy` for loading.

## Backwards Compatibility
- `ProjectService` retains existing behavior via a backward-compatible constructor overload which builds a default factory and resolver with empty loaders. Existing non-DI instantiations (like `App.xaml.cs`) continue working unchanged.
- `FileService` still provides sync read/write methods; async variants are wrappers for now. We can later replace sync implementations with true async IO in a single-revision change.

## Migration Plan / Phases
- Phase 1 — Add abstractions (current changes)
  1. Add `ILoadStrategy`, `ITranslationStrategyFactory`, `IProjectModeResolver`, `ProjectTypeVariant`.
  2. Implement `JsonLoadStrategy`, `NamespacedLoadStrategy` and `YamlLoadStrategy` placeholder.
  3. Add `TranslationStrategyFactory` and `ProjectModeResolver` services. Keep existing `ProjectService` constructor backward-compatible.
  4. Add async method signatures to `IFileService` and `ISaveStrategy`; provide wrappers in `FileService` and `SaveStrategies`.
- Phase 2 — Adopt DI and register strategy implementations
  1. Update composition root to register `ISaveStrategy` (existing), `ILoadStrategy` implementations, `ITranslationStrategyFactory`, `IProjectModeResolver`.
  2. Replace default factory fallback in `ProjectService` with DI-injected factory.
  3. Update UI/invocation layers to use new `IProjectService.Load` that selects loaders using the resolver.
- Phase 3 — Add manifest-based strategy & remove deprecated APIs
  1. Implement a concrete manifest loader that reads `toucan.project` -> `translationPackages` and loads translation files defined in the manifest.
  2. Add `LoadAsync` and `SaveAsync` first-class methods across interfaces (instead of wrappers), and migrate code paths.
  3. Mark old synchronous `IFileService` and `ISaveStrategy.Save` as `[Obsolete]` with guidance to use async methods.
  4. Remove `ProjectHelper` and other global static helpers (if present).

## Migration Notes
- When adding manifest-based loading, decide precedence: manifest should override folder scan. There may be a need to merge a manifest and discovered files; pattern: manifest files take precedence over scanned file paths.
- When switching to `LoadAsync` and `SaveAsync`, ensure UI code adapts to `await` or keeps legacy calls via synchronous wrappers for compatibility.
- Consider adding `IParserFactory` at this stage for platform extensibility (JSON, PO, RESX) and a `ISaveStrategy`/`ISaveFormat` mapping from manifest file types to strategies.

## Minimal Change Guidance for Maintainers
- Keep existing file IO sync methods while adding async wrappers to avoid immediate breaking changes in the UI layer.
- Register `ILoadStrategy` implementations in the DI composition root (or provide them to `ProjectService` as part of the fallback constructor if DI isn't present).
  Example:
  - JsonLoadStrategy(jsonFileService)
  - NamespacedLoadStrategy(new JsonLoadStrategy(...))
  Example DI registration (ASP.NET or Microsoft.Extensions.DependencyInjection style):

  ```csharp
  services.AddSingleton<IFileService, FileService>();
  services.AddSingleton<ISaveStrategy, JsonSaveStrategy>();
  services.AddSingleton<ISaveStrategy, NamespacedSaveStrategy>();
  services.AddSingleton<ISaveStrategy, YamlSaveStrategy>();

  services.AddSingleton<ILoadStrategy, JsonLoadStrategy>();
  services.AddSingleton<ILoadStrategy, NamespacedLoadStrategy>();
  services.AddSingleton<ILoadStrategy, ManifestLoadStrategy>();

  services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
  services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();
  services.AddSingleton<IProjectService, ProjectService>();
  ```

### Integration status (current branch)

- App now builds a DI container at startup and assigns it to `App.Services` for hybrid access from non-DI code. See `App.xaml.cs`.
- `MainWindowViewModel` now resolves `NewProjectPrompt` via `App.Services` when available (fallback to old constructor remains).
- `StartScreenViewModel` `NewProject` command also resolves `NewProjectPrompt` using `App.Services`.
- Logging has been wired using `Microsoft.Extensions.Logging` and `ILogger<T>` is injected into key services:
  - `FileService`, `JsonParser`, `JsonLoadStrategy`, `ProjectService`.
- The `TranslationStrategyFactory` and `ProjectModeResolver` are implemented and registered.

This is an incremental migration: the goal is to enable progressive DI adoption without immediately breaking the app.
- Add logging with `ILogger<T>` across services where currently `Console.WriteLine` is used.

## TODO / Follow-ups
- Implement manifest-based `IProjectManifestLoader` and `IProjectManifest` models to represent `toucan.project`.
- Migrate `ProjectService.Load` to prefer a `manifest` mode with full language file mapping.
- Convert `FileService` to real async IO using `File.ReadAllTextAsync`.

---
This document should be included in the repository to guide the next steps of migration and help reviewers understand the change footprint.
