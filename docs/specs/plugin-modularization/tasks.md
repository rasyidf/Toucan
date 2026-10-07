---
title: "plugin-modularization — tasks"
status: done
progress: "9/9 steps"
updated: 2026-10-07
summary: "Nine ordered steps from a snapshot safety net to removing the static fallbacks; the build stays green after each."
---
# Tasks: plugin modularization

Each step leaves `dotnet test Toucan.CrossPlatform.slnx` green. `WpfParityTests.CutKeyValues_RemovesTheKeyAfterConfirming` fails intermittently, including on the baseline run before any change (2 of 4 full runs); it is unrelated to this work.

Notes from steps 1-3: `ScanContext` and `ScanProgress` moved to Common with `FileEnumerator` (it depends on them). Built-in modules are listed in `IPluginCatalog.BuiltInModules`, not `Plugins`, so the trust and enable UI is untouched. `AddToucanCore` already wraps formats, frameworks, providers and validation as four modules (`toucan.*`); steps 4-7 move each into its own assembly.

Notes from step 4: `ValidationPipeline` and the user-configured `CustomValidationRule` stay in Core; only the six built-in rules moved. The rule `Name` values were set to the labels the Settings page already showed, so the UI text is unchanged. `OptionsViewModel` takes an optional `IValidationPipeline` and shows no rules without one. `Toucan.Modules.Defaults` was created here instead of in step 8.

Notes from step 5: the eight profiles moved unchanged (they only use Abstractions). `AddToucanFrameworks` was removed from Core. The catalog lists modules in registration order, which now depends on which assembly registers them, so tests compare the set of IDs, not the order.

- [x] 1. Safety net
  - [x] 1.1 `ModuleSnapshotTests`: IDs and order of formats, providers, rules, profiles; provider definitions
  - [x] 1.2 `ArchitectureTests`: Core references no `Toucan.Modules.*`; Abstractions does not reference Core
  - _Requirements: R1.3, R3.1_
- [x] 2. Extract `Toucan.Core.Common` (`IFileService`, `FileService`, `FileEnumerator`, `NestedJsonParser`); add to both solutions
  - _Requirements: R1_
- [x] 3. Module seam in Core: `BuiltInModule`, `AddToucanModule`, catalog source `BuiltIn`; explicit manifest, default-provider and ordering contracts
  - _Requirements: R2, R3.3_
- [x] 4. Migrate Validation (`Toucan.Modules.Validation`); build Settings → Validation from the registry
  - _Requirements: R2, R6_
- [x] 5. Migrate Frameworks (`Toucan.Modules.Frameworks`)
- [x] 6. Migrate Providers (`Toucan.Modules.Providers`)
- [x] 7. Migrate Formats: Json first (Json, Namespaced, Manifest, Arb), then Xml, Text, Data
  - _Requirements: R4_
- [x] 8. Remove static fallbacks; point tests at `FormatTestHost` / `ToucanDefaults`
  - _Requirements: R5_
- [x] 9. Docs and packaging: `docs/plugins.md`, `Toucan.Core/ARCHITECTURE.md`, `docs/ARCHITECTURE.md`, packaging scripts

## Outcome

Notes from steps 6-9:

- Providers: `Toucan.Modules.Providers` holds all eight (Mock included). `PretranslationService.DefaultProviderName` keeps Google the default.
- Formats: four modules, `toucan.formats.json|xml|text|data`. `AddFormatStrategy` (Common) registers a strategy once and forwards the interface, as the old private helper did. The factory orders `SaveStrategies` by the shipped order of the built-in IDs, so pickers and `toucan list-formats` do not depend on which module registers first; plugin formats follow.
- Static fallbacks removed: `BuiltInFormats`, `BuiltInProviders`, `FrameworkDetector`, `TranslationProviderRegistry()` and the built-in path in `ProjectSettings` (now `{language}.json` when no resolver is set; `ProjectService` always sets one). `CommentPersistenceService` without a factory treats every format as needing a sidecar. Tests use `FormatTestHost`, which resolves from the real composition.
- Packaging: no script changes. `dotnet publish` of the app and CLI copies every module assembly (verified for the CLI); the installer and MSIX take the whole publish folder.
- Core now contains no concrete format, provider, rule or profile; `ArchitectureTests` enforces that Core, Common and the modules keep to the dependency rules.
- Deferred, as listed in requirements: shipping a module as an external plugin folder, per-format assemblies, replacing the static `HttpClient` fields.
- Pre-existing flakiness, not caused by this work: three headless UI tests (`TypingIntoACard...`, `CutKeyValues...`, `GhostText...Accept...`) fail intermittently. On an untouched checkout of the commit this work started from (`233ce2b`), 9 of 10 full runs of `Toucan.Avalonia.Tests` had one to three of exactly these tests failing; with all the changes above the rate was about half of the runs. Disabling xunit parallelization did not fix them. The `TypingIntoACard` failure is the typed text never reaching the focused box (`box.Focus()` returned true, `box.Text` stayed empty), so the cause looks like headless focus or keyboard timing. Fixing them is a separate task.
