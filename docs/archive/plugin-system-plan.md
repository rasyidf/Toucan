---
title: "Plugin System Plan (.dll assemblies)"
status: done
progress: "6/6 phases"
updated: 2026-10-01
summary: "Plugin system plan and implementation log (FormatId migration, abstractions package, loader, trust, sample). All six phases done; author guide is docs/plugins.md."
archived: 2026-10-07
reason: "All six phases shipped in v0.18.0; the author guide is docs/plugins.md"
---
# Plugin System Plan (.dll assemblies)

> Status: IMPLEMENTED (preview). Phases 1-6 are done for the Avalonia app and the CLI; see the progress log at the end and [docs/plugins.md](../plugins.md) for the author guide.
> Supersedes the "v2.0 Plugin System" stub at the end of `panel-extension-plan.md`.

## Decisions

| # | Decision |
|---|----------|
| 1 | Formats are keyed by a string `FormatId`, not the closed `SaveStyles` enum. |
| 2 | Signing is a stub for now (interface + "unsigned" result, never blocks). It becomes mandatory once the app has collaboration and auth; today it is a local editor. Trust is a first-load prompt plus an enable/disable list. |
| 3 | v1 is Core-only plugins: formats, providers, validation rules, framework profiles, CLI support. UI panels come later. |
| 4 | Target Avalonia + CLI only. The legacy WPF `Toucan/` project is owned by a separate effort and is out of scope. |

## Current state (why this is not just a loader)

Most extension points are already `IEnumerable<T>` injected from `App.ConfigureServices`:
`ITranslationProvider`, `IValidationRule`, `IFrameworkProfile`, `ISaveStrategy`, `ILoadStrategy`.

Closed spots that block plugins:
1. `ISaveStrategy.Style` / `ILoadStrategy.Style` are `SaveStyles`. The enum is persisted in `ProjectSettings.SaveStyle`, and `ProjectSettings.ResolveDefaultPath` switches on it.
2. `TranslationProviderRegistry` returns a static `s_builtIn` list, so plugin providers would not appear in provider settings.
3. `Toucan.CLI/Program.cs` does not use DI (`CreateProvider` is a `switch`; rules are `new`-ed by hand).
4. `FrameworkDetector` is static with a hard-coded extension table.
5. Composition lives in `Toucan.Avalonia/App.axaml.cs`, so the CLI cannot share it.

## Phases

### Phase 1: Abstractions package
New project `Toucan.Plugins.Abstractions` (netstandard2.1 or net10.0, no Avalonia, no Toucan.Core reference).
- `IToucanPlugin { void Initialize(IPluginContext ctx); }`
- `IPluginContext`: `AddFormat`, `AddProvider`, `AddValidationRule`, `AddFrameworkProfile`, `Logger`, `HostApiVersion`. No raw `IServiceProvider`.
- Move or re-expose the contracts plugins need (`ITranslationProvider`, `IValidationRule`, `IFrameworkProfile`, format strategies, `TranslationItem`, `ProjectSettings` view). Core references Abstractions, not the reverse.
- `plugin.json` model: `id`, `name`, `version`, `apiVersion`, `entryAssembly`, `author`, `capabilities[]`.
- Semantic `ApiVersion` constant; host accepts same major.

### Phase 2: FormatId migration (riskiest, do first, test-first)
Touches ~60 files: 14 load strategies, 14 save strategies, 8 framework profiles, 6 service contracts, `ProjectSettings`, `ProjectService`, `ProjectModeResolver`, `FrameworkDetector`, 2 Avalonia VMs, CLI, 3 test projects.

Steps:
1. Add `FormatId` (string, case-insensitive) with constants for the 14 built-ins (`"json"`, `"namespaced"`, `"po"`, ...).
2. Strategies expose `string FormatId`; keep `Style` as a compatibility shim on built-ins only.
3. Add `string DefaultFileName(string language)` and `string[] Extensions` to the strategy contract; delete the `ResolveDefaultPath` switch.
4. `ITranslationStrategyFactory`: add `GetSaveStrategy(string formatId)` / `GetLoadStrategy(string formatId)`; keep enum overloads delegating.
5. `ProjectSettings`: persist `saveFormat` (string). Read old `saveStyle` (enum name or int) and map to the new ID; write only the new key. Update `docs/toucan.project.schema.json`.
6. `FrameworkDetector`: make it an instance service driven by `Extensions` and `IFrameworkProfile.DetectionScore`; static `Detect` stays as a wrapper for built-ins until callers move.
7. Update UI format pickers (`MainWindowViewModel.File.cs`, `ProjectDialogViewModels.cs`) to list formats from the factory.

Tests before refactor: round-trip every built-in format; load a project file saved with the old enum key and assert it migrates; save and confirm the new key.

### Phase 3: Open registries and shared composition root
- `TranslationProviderRegistry` builds definitions from registered providers (providers expose a `ProviderDefinition`); built-in static list becomes the built-ins' own metadata.
- Move service registration into `Toucan.Core` (`AddToucanCore(this IServiceCollection)`), used by Avalonia and CLI. Core takes the `Microsoft.Extensions.DependencyInjection.Abstractions` package.
- CLI: replace manual construction and `CreateProvider` switch with DI; `list-formats` lists plugin formats.

### Phase 4: PluginHost (loader)
- Discovery roots: `<appdata>/Toucan/plugins/<id>/plugin.json`. Per-project `.toucan/plugins` deferred.
- One collectible `AssemblyLoadContext` per plugin, `AssemblyDependencyResolver` for its deps. Share `Toucan.Plugins.Abstractions` (and any types in its signatures) from the default context so type identity holds.
- Validate manifest, `apiVersion`, and enabled state; wrap `Initialize` in try/catch; collect `PluginLoadResult { Id, Status, Error }`.
- Runs before `BuildServiceProvider` (it uses `ValidateOnBuild = true`); `IPluginContext` calls become `services.AddSingleton<...>`.
- Duplicate IDs: built-ins win, plugin load fails with a clear error.
- Restart required to apply changes. No hot unload in v1.

### Phase 5: Trust and management
- `IPluginSignatureVerifier` stub returning `Unsigned`. Never blocks. Marked with a TODO referencing the collaboration/auth milestone.
- First-load prompt per plugin (id + version + file hash); decision stored in app preferences. Hash change re-prompts.
- Options page "Plugins": list, enable/disable, load errors, open plugins folder.
- CLI: `toucan plugins list`; plugins load only if already trusted (no prompt in CI), `--allow-plugin <id>` to override.

### Phase 6: Sample plugin, docs, tests
- `samples/Toucan.Sample.Plugin`: one custom format (e.g. `.strings`-like or simple `key=value`) plus one validation rule.
- Tests: load fixture plugin; broken plugin does not stop startup; apiVersion mismatch rejected; duplicate ID; project with plugin format ID round-trips; project opens with a clear message when its plugin format is missing.
- Docs: `docs/plugins.md` authoring guide; update `roadmap.json`, `future-roadmap.md`, `README.md` roadmap row.

### Later (not v1)
- UI contributions: `ISidePanel` + view factory in a separate `Toucan.Plugins.Avalonia` package.
- Per-project rules in `.toucan/rules/`.
- Mandatory signing, plugin feed/marketplace, hot unload.

## Open risks
- **Missing-plugin projects:** a project saved with a plugin format must open read-safe (no silent fallback to JSON that overwrites files). Needs an explicit "format unavailable" state.
- **Version skew:** Abstractions must stay small and stable; every type added is a compatibility promise.
- **Analyzers:** `AnalysisLevel=latest-all` and `TreatWarningsAsErrors` apply to new projects through `Directory.Build.props`; expect some friction in the loader code (reflection, `Assembly.LoadFrom` rules).

## Suggested order
Phase 2 tests -> Phase 2 migration -> Phase 1 (extract Abstractions) -> Phase 3 -> Phase 4 -> Phase 5 -> Phase 6.
(Phase 1 is listed first logically but is easier after FormatId settles the contract shape.)

## Progress log

### Phase 2 safety net: DONE
Tests in `tests/Toucan.Core.Tests/Formats/` (60 new, suite is 118 and green):
- `FormatRoundTripTests`: every built-in format saves and reloads the same keys/values; every enum value has exactly one save strategy.
- `ProjectSettingsFormatTests`: default path per format, `saveStyle` persisted as a number, legacy numeric load, enum round-trip.

Run: `dotnet test tests/Toucan.Core.Tests` (the project now targets plain `net10.0` and references `Microsoft.NET.Test.Sdk`, so it runs on macOS/Linux/Windows).

Existing quirks found and pinned (decide per item whether Phase 2 fixes them):
1. FIXED: `ProjectSettings.ResolveDefaultPath` had no case for PO, INI, `.properties` and Laravel PHP, so new projects got `en.json`. Now `{lang}.po`, `{lang}.ini`, `{lang}.properties`, `{lang}/messages.php`.
2. `ProjectSettings.LoadFrom` swallows every exception and returns null, so a project file with an unknown format is treated as "no project" and re-detected. This is the missing-plugin hazard.
3. `ProjectService.Load` falls back to the JSON loader when no strategy matches the format.
4. Namespaced writes a merged `{lang}.json` plus `locales/{lang}/{ns}.json`, and the loader reads both, so reload duplicates every item.
5. INI (`Adb`) has a save strategy but no load strategy.

### Phase 2 migration, step 1: DONE (FormatId + persisted string)
- `Toucan.Core/Models/FormatIds.cs`: constants, `FromStyle`, `TryGetStyle`, legacy-value mapping, case-insensitive comparer.
- `ISaveStrategy` / `ILoadStrategy` expose `string FormatId` instead of `SaveStyles Style` (all 28 built-ins updated). Plugins can now identify a format without touching the enum.
- `ITranslationStrategyFactory`: string lookups, `SaveFormatIds`; `SaveStyles` overloads kept as extension methods.
- `ProjectSettings`: persists `"saveFormat"` (string). Old `"saveStyle"` (number or enum name) is read once, mapped, and not written back; `"saveFormat"` wins if both exist. `SaveStyle` stays as a `[JsonIgnore]` shim (returns Json for plugin formats, so new code should use `SaveFormat`).
- Unknown format IDs now survive load and save instead of failing the whole project load (quirk 2 resolved for the "unknown format" case; invalid JSON still returns null).
- Tests: 152 in Core (was 118), 33 Avalonia (incl. DI `ValidateOnBuild` smoke tests), all green.

### Phase 2 migration, step 2: format-unavailable state: DONE
- `FormatUnavailableException` (Core/Models). `ProjectService.Load` throws it when the project's format ID has no load strategy and is not a built-in, before the manifest or JSON loaders can misread the files.
- `ProjectLifecycleService.OpenProjectAsync` returns `ProjectOpenStatus.FormatUnavailable` with the message; nothing is initialized or opened. Avalonia already shows `ErrorMessage` for non-success statuses. The CLI prints the message and exits 1.
- Built-in formats without a loader (INI) keep the legacy JSON fallback (quirk 5), pinned by a test. Reconsider when INI gets a loader.
- Not done yet: the Avalonia dialog says the message but does not offer "open plugins folder" (Phase 5). `LanguageManagementService` still calls `CreateLanguage` with the `SaveStyle` shim, which would write JSON for a plugin format; unreachable today because such projects cannot open, but fix when `CreateLanguage` takes a format ID.
- Tests: 157 Core, 33 Avalonia, green.

### Phase 2 migration, step 3: format conventions on the strategy: DONE (Phase 2 complete)
The same per-format table existed in four places (`ProjectSettings`, `ProjectLifecycleService`, `LanguageManagementService`, `CommentPersistenceService`) plus `FrameworkDetector` and two UI pickers. All now read from the strategy:
- `ISaveStrategy`: `DisplayName`, `FileExtensions`, `DefaultFilePath(language)` (required), `LanguageFiles(root, language)`, `StoresCommentsInline`, `CommentSidecarBase(language)`, `Detection` (all but `DefaultFilePath` have defaults, so a minimal plugin format is `FormatId` + `DefaultFilePath` + `Save`/`SaveAsync` + a load strategy).
- `FormatDetector` (instance, driven by `Detection` rules) replaces the static table; `FrameworkDetector.Detect` stays as a built-in wrapper.
- `IProjectService`: `GetDefaultFilePath`, `GetLanguageFiles`; `CreateProject`/`CreateLanguage`/`Save` take a format ID. `ProjectSettings.DefaultPathResolver` is set by `ProjectService` so `Save()` backfills plugin default paths; without it, built-ins resolve via `BuiltInFormats`.
- `IFrameworkProfile.DefaultFormat` is now `DefaultFormatId`. `ICommentPersistenceService` takes a format ID (`SaveStyles` extension overloads kept).
- Bug fixed on the way: Save As copied `SaveStyle`, which would have turned a plugin project into JSON. It now copies `SaveFormat` (test: `SaveAsKeepsAPluginFormat`).
- `CreateLanguage` and `Save` now throw `FormatUnavailableException` for an unknown format instead of writing JSON.
- Avalonia export picker lists registered strategies by `DisplayName` (order is now registration order, was a hand-written order); import maps extension through `FileExtensions`; new-project tiles carry `FormatId`.
- Not changed: `SaveStyles` remains for the built-in shim and the legacy migration. `TranslationFileFilters` (file-dialog patterns) is still a hand-written list.

### Phase 3: open registries and shared composition root: DONE
- `ToucanCoreServiceCollectionExtensions.AddToucanCore()` (`Toucan.Core/`): formats, providers, validation, `ProjectService`, comment persistence. Also `AddToucanFormats/Providers/Validation` individually. Hosts register logging themselves. Core now references `Microsoft.Extensions.DependencyInjection.Abstractions`.
- Avalonia `ConfigureServices` calls `AddToucanCore()` and keeps only UI/editor services. Registration order preserved (Google first; Json before Manifest).
- `ITranslationProvider.Definition` (default null). Built-ins carry their own definitions; `TranslationProviderRegistry` is built from registered providers, so plugin providers show up in settings. Mock has no definition and stays unlisted, as before. Parameterless ctor kept for container-less callers.
- CLI uses the same container. Side effects: Java `.properties` and Laravel PHP export now work in the CLI (they were never wired there), `list-formats` shows format IDs and names (legacy names such as `AndroidXml` still accepted by `export -f`), provider names come from the container, and a missing-plugin project prints the "format unavailable" message and exits 1.
- `BuiltInFormats` / `BuiltInProviders` exist for container-less callers; a test keeps `BuiltInFormats` in step with the container.
- Frameworks: `IFrameworkProfile`s were already resolved from the container (`DialogService` uses `GetServices<IFrameworkProfile>()`), so plugin profiles need no further work.
- Tests: 185 Core, 33 Avalonia, all green. CLI smoke-tested by hand (list-formats, stats, check, export in 4 formats, translate with mock, missing-plugin project).

Known leftovers
- `Toucan/` (WPF) and `tests/Toucan.Tests` still use the old signatures (`SaveStyles` on `IProjectService`, `ICommentPersistenceService`, `IFrameworkProfile.DefaultFormat`, `new TranslationProviderRegistry()` still compiles). Not buildable here; follow-up for whoever owns WPF.
- INI has no load strategy (quirk 5) and Namespaced reload duplicates items (quirk 4).
- Next: Phase 1 (extract `Toucan.Plugins.Abstractions`; the contracts above are now stable enough to move), then Phase 4 (loader).

### Phase 1: Toucan.Plugins.Abstractions: DONE
New project `Toucan.Plugins.Abstractions` (net10.0; only dependency is `Microsoft.Extensions.Logging.Abstractions`; added to the solution; referenced by Core). Types keep their namespaces (`Toucan.Core.Contracts`, `Toucan.Core.Models`, `Toucan.Extensions`), so no consuming code changed.

In the package: `ISaveStrategy`, `ILoadStrategy`, `ITranslationProvider`, `IValidationRule` (+ `ValidationContext`, `ValidationResult`, `ValidationSeverity`), `IFrameworkProfile` (+ `DiscoveredFile`), `TranslationItem`, `SaveContext`, `NsTreeItem`, `FormatIds`, `SaveStyles`, `FormatDetection`, `ProviderDefinition`, `Pretranslation*` models, `ChangeType`, `TranslationItemExtensions`.
New: `IToucanPlugin`, `IPluginContext` (`AddFormat`, `AddProvider`, `AddValidationRule`, `AddFrameworkProfile`, `Logger`, `PluginDirectory`, `HostApiVersion`), `PluginApi.Current` (1.0; compatible = same major, plugin minor <= host minor), `PluginManifest` (parse + validate `plugin.json`: id charset, version, apiVersion, entryAssembly must be a bare `.dll` name, known capabilities) and `PluginCapabilities`.

Kept in Core (host internals): `IValidationPipeline`, `IProjectService`, `ProjectSettings`, `IFileService`, registries, lifecycle.

Contract change: `ValidationContext.Settings` (a `ProjectSettings`) became `PrimaryLanguage` (string), since no rule used anything else and `ProjectSettings` cannot be in the abstractions. Updated Core, Avalonia and CLI. The WPF project (`Toucan/MainWindowViewModel.Translation.cs:759`) still uses the old shape.

Tests: 206 Core (21 new: manifest parsing/validation, API compatibility, a guard that the abstractions assembly never references Core, Avalonia or Mvvm, and that plugin-facing types live in it), 33 Avalonia, green. `dotnet build ToucanProject.slnx` fails on macOS only for the two WPF projects (`EnableWindowsTargeting`).

Next: Phase 4 (PluginHost). Open design points to settle there: how `IPluginContext` registrations are applied before `BuildServiceProvider` (collect, then `AddSingleton` instances), enforcing declared capabilities and format-ID/rule-ID collisions, and `AssemblyLoadContext` sharing rules (Abstractions + Logging.Abstractions shared from the host).

### Phase 4: PluginHost (loader): DONE (not yet wired into the apps)
Code in `Toucan.Core/Plugins/`:
- `PluginHost` + `services.AddToucanPlugins(PluginHostOptions)`: call after `AddToucanCore()` and logging, before `BuildServiceProvider`. Discovers `<root>/<folder>/plugin.json` (folders in name order), and returns an `IPluginCatalog` of `PluginLoadResult`s (`Loaded`, `Disabled`, `Rejected`, `Failed`, plus error text and what was registered).
- Reserved IDs (formats, providers, rules, profiles) are read from a throwaway container built from the current registrations, so built-ins are never duplicated in a list. Plugins cannot shadow a built-in or another plugin; collisions fail the later plugin.
- `PluginContext` collects registrations and enforces: declared capabilities, valid lowercase format IDs, matching save/load IDs, provider definition name match and no `IsBuiltIn` claim, no double registration. Registrations are applied only if `Initialize` returns cleanly and all are valid, so a failing plugin contributes nothing and does not claim its IDs.
- `PluginLoadContext`: one non-collectible `AssemblyLoadContext` per plugin with `AssemblyDependencyResolver`; `Toucan.Plugins.Abstractions`, `Toucan.Core` and the Microsoft.Extensions abstractions always resolve from the host (extra names via `PluginHostOptions.SharedAssemblies`). Verified: plugin assembly is in `plugin:<id>`, no private copy of the abstractions, host's `ISaveStrategy` is the plugin's.
- Manifest gained optional `entryType` (needed when an assembly has several plugins; otherwise exactly one public `IToucanPlugin` with a parameterless constructor is required).
- Per-plugin failures (exceptions in the constructor, `Initialize`, assembly loading, bad manifest, wrong API version, missing/garbage assembly) never stop the app or other plugins.
- Default plugin folder: `Documents/Toucan/plugins` (`PluginHostOptions.DefaultRoot()`, next to `settings.json`).
- Core now references the full `Microsoft.Extensions.DependencyInjection` package (needed for the probe container).

Test fixtures: `tests/Plugins/Toucan.TestPlugins` (a real `.tfmt` format, provider, rule, profile and a set of misbehaving plugins) is built as a project reference and copied to `TestPlugins/` next to the Core test assembly. `PluginHostTests` (35 tests) load it from a temp folder the way an installed plugin loads, including an end-to-end run: create, save, reopen and auto-detect a project in the plugin format, and "format unavailable" when the plugin is disabled.

Bug found by the tests while writing this: the logger factory came from the throwaway container and was disposed before loading finished (`ObjectDisposedException` on every failure path). Fixed by keeping the container alive during the load.

Not done on purpose: `AddToucanPlugins` is not called by Avalonia or the CLI yet. Loading arbitrary code from a user folder must wait for Phase 5's trust gate (hash + first-load prompt, disable list, signature stub), so that is where both hosts get wired up.

Known limits (v1): restart required to apply plugin changes; no unloading; plugin classes are created with `Activator` (no DI into plugins); `Initialize` is synchronous with no timeout; plugins cannot see `IFileService`/`IProjectService` (they use `System.IO`).

Tests: 241 Core, 33 Avalonia, green.

### Phase 5: trust and management: DONE (plugins now load in the app and CLI)
Core (`Toucan.Core/Plugins/PluginTrust.cs`, host gate in `PluginHost`):
- `PluginHasher`: SHA-256 over every file in the plugin folder (relative path, length, content), so any edit, addition or rename changes the hash.
- `FilePluginPolicyStore` (`Documents/Toucan/plugin-policy.json`): per-plugin enabled flag, trusted content hash, and "don't ask again" per hash. Atomic writes, a corrupt or missing file means "nothing trusted, everything enabled". Trusting replaces any earlier hash; trusting clears a dismissal.
- Gate in `PluginHostOptions.Policy`: a plugin loads only if enabled and its exact content is trusted. Otherwise it is `Disabled` or the new `NeedsTrust` (`Trust` = `Untrusted` or `Changed`), and none of its code runs (no load context is created). Disabled wins over trust, so a disabled plugin never prompts. `AllowForThisRun` waives trust for one run without saving anything and does not override disabled.
- `IPluginSignatureVerifier` stub (`UnsignedPluginSignatureVerifier`, always `NotSigned`, never blocks), documented with the TODO that signing becomes mandatory with collaboration/auth. An `Invalid` signature is rejected before any code runs; a `Valid` one does not replace trust. Results carry `ContentHash`, `Signature`, `Trust`.

CLI: loads the same policy file and only enabled, trusted plugins; never prompts. `toucan plugins list|trust|revoke|enable|disable <id>`, `--allow-plugin <id>` (this run only), a stderr note when untrusted plugins were skipped, and `TOUCAN_PLUGINS_DIR` / `TOUCAN_PLUGIN_POLICY` to relocate the folder and policy file (CI, testing). `trust` is the explicit consent step for headless use. Smoke-tested by hand: list, trust, load, disable/enable, revoke, `--allow-plugin` (leaves the policy file empty), unknown id and bad subcommand.

Avalonia: `AddToucanPlugins` runs in `ConfigureServices` (`pluginRoot` / `pluginPolicyPath` hooks keep tests off the real Documents folder). Settings gets a Plugins page (index 10, About moved to 11; `OptionsViewModel.PluginsPage`): per plugin the name, version, author, description, status, what it provides, signature wording ("Not signed"), short hash, folder, an enable switch, Trust… (confirmation dialog stating that plugins run code with your permissions and showing the hash and signature), Revoke trust, Show folder, and a restart-required banner. Changes there apply to the policy immediately and take effect after restart. Menu: Settings → Plugins… (Project menu on macOS). After the project opens, a startup prompt lists untrusted or changed plugins (Review plugins / Don't ask again / Later); it never trusts anything itself.

Tests: 265 Core (+24: hashing, store, gate, signatures), 48 Avalonia (+15: prompt, page view model, headless render of the page). Layout checked from a rendered frame.

Known limits: restart required for any change; trust is verified by hashing right before loading (an attacker who can write your plugin folder can already run code as you); no per-capability prompt (the manifest's declared capabilities are shown only through "Provides"); signing is a stub; the WPF app has no plugin UI.

Next: Phase 6 (sample plugin, authoring guide `docs/plugins.md`, README/roadmap updates, packaging of `Toucan.Plugins.Abstractions` as a NuGet package, CI note for the cross-platform solution).

### Phase 6: sample plugin, docs, packaging: DONE (plan complete)
- `samples/Toucan.Sample.Plugin`: a real plugin (tab-separated-values format `sample-tsv` with escaping, detection and a `sample.todo-marker` rule), `plugin.json` and project file written to be read as documentation. It is in both solutions. `SamplePluginTests` (10) install its build output as a user would and drive it through the host: manifest validity, trust gate, project round trip with tabs/newlines/backslashes, detection, import by extension, malformed input, rule results.
- `Toucan.Plugins.Abstractions` is a NuGet package: version 1.0.0 tracks the plugin API, XML docs for IntelliSense (CS1591 off), README, MIT, nuspec checked. Verified from outside the repo: the sample builds against the packed `.nupkg` alone, and the result loads and works in the CLI (detect, `check` shows the rule, `export`, `plugins trust`). Finding: authors must use `ExcludeAssets="runtime"` on the package reference, otherwise Toucan.Plugins.Abstractions and the logging/DI abstractions are copied into the plugin folder (harmless, ignored by the loader, but untidy and part of the trust hash). The guide and sample say so.
- `docs/plugins.md`: quick start, manifest reference, each contribution type with its members, load/isolation rules, trust and signing, versioning, testing with the CLI, a troubleshooting table, limits, maintainer notes (cross-platform solution, packing).
- Docs updated: README roadmap row, `CHANGELOG.md` (Unreleased), `docs/todos/future-roadmap.md` (plugin items ticked, remaining ones listed), `panel-extension-plan.md` (stub superseded). `roadmap.json` left alone: its counters track WPF parity and the plugin entries carry no status field.
- Honest gap found while documenting: plugin validation rules run (validate-on-save, `toucan check`) but the Validation settings page lists a fixed set of built-in rules, so they have no enable/severity switch. Logged in the guide and the roadmap.

Final state: 275 Core tests, 48 Avalonia tests, all green on macOS via `dotnet test Toucan.CrossPlatform.slnx`.

Left for later (also in future-roadmap.md): UI contributions in a separate package, per-project `.toucan/rules`, per-rule settings for plugin rules, mandatory signing and a feed, hot reload/unload, WPF follow-up (`Toucan/` and `tests/Toucan.Tests` still use the old signatures), the INI loader and the Namespaced duplicate-on-reload quirk.
