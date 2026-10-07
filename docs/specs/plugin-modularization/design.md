---
title: "plugin-modularization — design"
status: done
progress: "9/9 steps"
updated: 2026-10-07
summary: "Target assembly layout, the shared Common assembly, the built-in module seam and the explicit contracts that replace order and name matching."
---
# Design: plugin modularization

## Decision: built-ins are not loaded through PluginHost

`PluginHost` needs a folder, a manifest and a trust prompt, has no DI (the strategies need `IFileService` and `ILogger`), forbids `IsBuiltIn` on definitions, and loads by reflection while the MSIX build uses ReadyToRun. Built-ins are therefore compiled-in project references that **register through the same shape** and appear in the same catalog.

## Target layout

```
Toucan.Plugins.Abstractions   contracts (unchanged, API 1.0)
Toucan.Core.Common            IFileService, FileService, FileEnumerator, NestedJsonParser, ScanContext, and the module seam (AddToucanModule)
Toucan.Core                   project services, PluginHost, registries; references no module
Toucan.Modules.Formats.Json   json, namespaced, manifest, arb
Toucan.Modules.Formats.Xml    android-xml, xliff, resx
Toucan.Modules.Formats.Text   po, ini, java-properties, ios-strings, laravel-php, csv
Toucan.Modules.Formats.Data   yaml, toml
Toucan.Modules.Providers      Google, DeepL, Microsoft, OpenAI, Claude, Gemini, Custom, Mock
Toucan.Modules.Validation     the 6 built-in rules
Toucan.Modules.Frameworks     the 8 profiles
Toucan.Modules.Defaults       AddToucanDefaults(): every module above
```

Dependencies point down only: modules → Common → Abstractions; Core → Common → Abstractions; apps and the CLI → Core + Defaults. Core and the modules never reference each other, which is why the module seam (`BuiltInModule`, `AddToucanModule`) lives in Common: a module needs it and must not reference Core. `ArchitectureTests` enforces both directions.

`Toucan.Modules.Defaults` exists from the first migrated module (step 4), not step 8: Core can no longer register the migrated rules itself, so every host calls `AddToucanCore()` then `AddToucanDefaults()`, before `AddToucanPlugins`. Each later step adds one line to Defaults.

Namespaces do not change (`Toucan.Core.Services`, `Toucan.Core.Contracts.Services`), so moving a file never edits its callers.

## Common

Holds the non-contract helpers that format modules and Core both need. Not a compatibility promise: only built-in modules and Core use it, external plugins keep to Abstractions. `NestedJsonParser` becomes public because modules are separate assemblies.

## Module seam

```csharp
// Toucan.Core.Common (namespace Toucan.Core.Plugins)
public sealed record BuiltInModule(string Id, string Name, IReadOnlyList<string> Capabilities);

public static IServiceCollection AddToucanModule(this IServiceCollection services, BuiltInModule module,
    Action<IServiceCollection> register);
```

`AddToucanModule` runs `register`, records the descriptor, and `IPluginCatalog` exposes it as a `PluginLoadResult` with `Source = BuiltIn`, status `Loaded`. What the module registered is computed from the services it added (the diff of the collection before and after), so the summary cannot drift from the registrations. `PluginHost.ReadReservedIds` already reads IDs from the container, so built-in IDs stay reserved without changes.

## Making implicit contracts explicit

| Today | Becomes |
|---|---|
| `GetManifestLoadStrategy()` matches `GetType().Name.Contains("Manifest")`; Manifest reports `FormatId = "json"` | A marker `IManifestLoadStrategy : ILoadStrategy` in Core.Common (not Abstractions: it is not part of the plugin API); the factory finds it by interface |
| "Json before Manifest" decides which loader `GetLoadStrategy("json")` returns | The factory prefers strategies that are not `IManifestLoadStrategy` for plain lookups |
| "Google first": pretranslation falls back to the first registered provider | Fallback is the provider named by a `DefaultProviderName` option set to `Google`, not list position |
| Detection order is the order of the registration list | Already explicit through `FormatDetection.Priority`; a test pins it |

## Static fallbacks

`BuiltInFormats`, `BuiltInProviders`, `FrameworkDetector`, `TranslationProviderRegistry()` and `ProjectSettings.BuiltInFormatPath` exist for container-less callers. After migration they are replaced by `ToucanDefaults.CreateServices()` in Defaults (used by tests and any tool), and `ProjectSettings` requires the resolver or falls back to `{language}.json` only when none is set.

## Verification

- `ModuleSnapshotTests` records IDs and order of formats, providers, rules and profiles, plus provider definitions, and must match before and after each step.
- `ArchitectureTests` checks assembly references (R1).
- The full `dotnet test Toucan.CrossPlatform.slnx` runs at every step.

## Risks

| Risk | Mitigation |
|---|---|
| Hidden order dependence | Snapshot test before the first move; explicit contracts above |
| Many tests construct concrete strategies | `FormatTestHost` becomes the single place; other tests use it |
| Packaging misses new assemblies | Check `installer.iss`, `Build-Msix.ps1`, `build-macos-app.sh`; the apps reference Defaults so MSBuild copies them |
| Analyzer friction (`latest-all`, warnings as errors) in new projects | New projects inherit `Directory.Build.props`; fix, never suppress |
