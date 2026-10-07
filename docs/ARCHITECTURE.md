---
title: "Toucan — Architecture"
status: active
updated: 2026-10-07
summary: "Layers, dependency graph, DI composition, plugin host and file layout of Toucan.Core, Avalonia app and CLI. Reflects v0.19.0 (Avalonia on all platforms; WPF removed)."
---
# Toucan — Architecture

> Last updated: 2026-10-01 (plugin system, string format IDs, shared composition root)

---

## High-Level Dependency Graph

```
┌─────────────────────────────────────────────────────────────────┐
│                         Consumers                                │
├──────────────┬──────────────────────┬───────────────────────────┤
│  Toucan.CLI                     │  Toucan.Avalonia          │
│  (Console)                      │  (Windows/macOS/Linux)    │
│  .NET 10                        │  .NET 10                  │
│  No UI deps                     │  Avalonia 12.0.5          │
│                                 │  FluentAvaloniaUI 3.0     │
│                                 │  MS.Ext.DI                │
└──────────────┬──────────────────┴───────────┬───────────────┘
               │                              │
               └──────────────┬───────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │     Toucan.Core       │
              │     .NET 10           │
              │                       │
              │  CommunityToolkit.Mvvm│
              │  ClosedXML            │
              │  MS.Ext.DI + Logging  │
              └───────────┬───────────┘
                          │
                          ▼
         ┌─────────────────────────────────┐        ┌──────────────────────┐
         │   Toucan.Plugins.Abstractions   │◄───────│  plugin assemblies   │
         │   plugin contract + NuGet pkg   │        │  (loaded at runtime) │
         └─────────────────────────────────┘        └──────────────────────┘
```

Both consumer projects reference `Toucan.Core`, which references `Toucan.Plugins.Abstractions` (the small, stable contract that plugins build against; it has no UI or Core dependencies). The core has no UI dependencies. Its composition root, `AddToucanCore()` (`ToucanCoreServiceCollectionExtensions`), registers formats, providers, validation and the project service; the Avalonia app and the CLI both call it, then add their own services.

---

## Layer Overview

### Toucan.Core

Platform-agnostic library containing all business logic:

- **Models** — `TranslationItem`, `NsTreeItem`, `ProjectSettings`, `FormatIds`, `SidePanel`, `StatusBarPanel`, and DTOs. The plugin-facing ones (`TranslationItem`, `SaveContext`, `FormatIds`, `ProviderDefinition`, …) live in `Toucan.Plugins.Abstractions` under the same namespaces.
- **Contracts** — interfaces for every service (`ILoadStrategy`, `ISaveStrategy`, `IProjectLifecycleService`, `ITranslationProvider`, etc.). The plugin-facing contracts (`ISaveStrategy`, `ILoadStrategy`, `ITranslationProvider`, `IValidationRule`, `IFrameworkProfile`) are in `Toucan.Plugins.Abstractions`; host internals (`IValidationPipeline`, `IProjectService`, …) stay in Core.
- **Services** — implementations: strategy factory, project lifecycle, translation management, validation, TM, audit, auto-save, fuzzy search
- **Format Engine** — 14 load strategies + 14 save strategies, identified by string format ID (`json`, `android-xml`, …). Each save strategy also owns the format's layout conventions (see below).
- **Framework Profiles** — 8 auto-detection profiles (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, Generic JSON)
- **Translation Providers** — Google, DeepL, Microsoft, OpenAI, Claude, Gemini, Custom Webhook, Mock; each carries its own settings definition
- **Validation** — 6 rules + pipeline
- **Plugins** — `PluginHost` (discovery, isolated load contexts, registration), trust policy store, content hasher, signature seam (see [Plugin System](#plugin-system))

**Dependencies:** CommunityToolkit.Mvvm 8.4.2, ClosedXML 0.105.0, Microsoft.Extensions.DependencyInjection 10.0.9, Microsoft.Extensions.Logging.Abstractions 10.0.9, `Toucan.Plugins.Abstractions`

### Toucan.CLI

Headless console tool for CI/CD integration.

- **9 commands:** `check`, `stats`, `translate`, `export`, `list-formats`, `list-keys`, `get`, `set`, `plugins` (`list`, `trust`, `revoke`, `enable`, `disable`)
- Same composition root as the app (`AddToucanCore()` + `AddToucanPlugins()`), so every registered format, provider and rule (including plugins') is available
- Plugins load only if enabled and already trusted; the CLI never prompts (`--allow-plugin <id>` waives trust for one run)
- Outputs: color console (progress bars), JSON (get), exit codes for CI

**Dependencies:** Toucan.Core, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

### Toucan.Avalonia

Desktop client for Windows, macOS and Linux (preview; Windows since v0.19.0). Targets `net10.0`. The older WPF app (last release v0.17.3) is on the `legacy/wpf` branch.

- **Framework:** Avalonia 12.0.5 + FluentAvaloniaUI 3.0.0
- **State:** Zen mode, Editor/Review/Audit modes, search and bulk edits, side panels (Explorer, Search, Issues, Source Code, Translation Memory, Languages, Inspector), plugins. Not ported yet: the Source Control, Translation and Dictionary panels.
- **Packaging:** `packaging/build-macos-app.sh` (ad-hoc-signed `Toucan.app`); Linux builds are `dotnet publish -r linux-x64|linux-arm64 --self-contained` tarballs.
- Full DI setup: `AddToucanCore()` + `AddToucanPlugins()` plus UI and editor services; Settings → Plugins page and a startup prompt for untrusted plugins
- `MainWindowViewModel` split into partials (File, Edit, Nav, Search, Bulk, Translation); dialogs via Avalonia StorageProvider
- Provider secrets: DPAPI on Windows, AES-GCM with a per-user key file on macOS/Linux (`SecureStorageService`)

**Dependencies:** Avalonia 12.0.5, FluentAvaloniaUI 3.0.0, CommunityToolkit.Mvvm 8.4.2

---

## Key Data Flows

### Project Load

```
User opens folder/file
    │
    ▼
IProjectLifecycleService.OpenAsync(path)
    │
    ├─► IUnsavedChangesHandler.Prompt() [if current project dirty]
    │
    ▼
IProjectModeResolver.Resolve(path)
    │
    ├─► FolderScan: detect framework via IFrameworkProfile.DetectionScore()
    │   └─► FormatDetector: pick the format ID whose strategy `Detection` rule matches (plugins included)
    │
    └─► ConfigManifest: read toucan.tproj → get saveFormat + package URLs
        (an unknown format ID throws FormatUnavailableException → "format unavailable", nothing is opened)
    │
    ▼
IProjectService.LoadProject(path, settings)
    │
    ├─► ITranslationStrategyFactory.GetLoadStrategy(style)
    │   └─► strategy.Load(folder) → IEnumerable<TranslationItem>
    │
    ├─► ICommentPersistenceService.LoadComments() [sidecar .json]
    │
    └─► IAuditService.LoadFromSidecar() [.toucan-metadata.json]
    │
    ▼
ITranslationManagementService.Initialize(items)
    │ (sets baselines for dirty tracking)
    │
    ▼
IFileWatcherService.Watch(folder)
IAutoSaveService.Start()
    │
    ▼
ProjectChanged event → UI rebuilds tree, summary, page data
```

### Edit (Value Change)

```
User types in TranslationItemView
    │
    ▼
TranslationItemViewModel.Value setter
    │
    ▼
ITranslationManagementService.NotifyValueChanged(item)
    │
    ├─► Compare against TranslationBaseline → update IsDirty
    ├─► IAuditService.RecordChange(item, ChangeType.DirectEdit)
    └─► IUndoRedoService.Push(EditAction)
    │
    ▼
MainWindowViewModel.HasUnsavedChanges → title bar indicator
StatusBarService.UpdateSessionDirtyCount()
```

### Save

```
User triggers Save (Ctrl+S)
    │
    ▼
IProjectLifecycleService.SaveAsync()
    │
    ▼
ITranslationStrategyFactory.GetSaveStrategy(style)
    │
    ▼
ISaveStrategy.SaveAsync(path, SaveContext)
    │ (SaveContext bundles: items, settings, languages, tree)
    │
    ├─► Format-specific file writing (one file per language or single file)
    ├─► ICommentPersistenceService.SaveComments()
    └─► IAuditService.SaveToSidecar()
    │
    ▼
ITranslationManagementService.ResetBaselines()
IValidationPipeline.RunAll() [on-save validation]
    │
    ▼
IsDirty = false → UI updates
```

### Pre-Translate

```
User opens Pre-Translate dialog
    │
    ▼
PreTranslateViewModel collects:
    - Target languages
    - Provider selection
    - Scope (all / namespace / language)
    - Options (overwrite, formality, context)
    │
    ▼
IPretranslationService.TranslateAsync(request)
    │
    ├─► Build PretranslationJob list (missing items only, unless overwrite)
    ├─► ITranslationProviderRegistry.Get(providerId)
    │
    ▼
ITranslationProvider.PretranslateAsync(jobs, options, progress, ct)
    │
    ├─► Batch API calls with placeholder preservation
    ├─► Progress reporting → UI progress bar
    │
    ▼
TranslationPostProcessor.Process(results)
    │ (placeholder restoration, whitespace normalization)
    │
    ▼
Preview results in dialog (dry-run mode)
    │
    ▼ [user commits]
ITranslationManagementService.ApplyResults(results)
    │ (marks items as ChangeType.Suggestion)
    │
    ▼
UI refresh, dirty state updated
```

---

## Panel System Architecture

### Design

VS Code-inspired modular panel layout with two activity bars (left + right), each hosting switchable panels. Layout state persists across sessions.

### Type Hierarchy

```
ISidePanel (Core contract)
    │   Id, Title, Icon, DefaultSlot (Left/Right), Order, IsActive, IsVisible
    │
    ├── SidePanelBase (Core) — ObservableObject base class
    │
    └── BuiltInSidePanel (app) — concrete registration instances
```

### Registry & Service

| Type | Layer | Role |
|------|-------|------|
| `SidePanelRegistry` | Core | Singleton. Register/Unregister/Activate/Toggle. Tracks `ActiveLeftPanel` + `ActiveRightPanel`. Fires `PropertyChanged`. |
| `PanelService` | App | Singleton. Wraps registry. Adds toggle commands, zen mode, layout persistence (`~/Documents/Toucan/layout.json`). |

### Flow

```
App startup → Register 10 BuiltInSidePanel instances
    │
    ▼
SidePanelRegistry sorts into LeftSlotPanels / RightSlotPanels (by Order)
First panel in each slot auto-activated
    │
    ▼
ActivityBar (UserControl) binds to panel collection
    │ Renders vertical icon strip (36px wide)
    │ Context menu: toggle panel visibility
    │
    ▼ [user clicks icon]
PanelService.ActivateLeftPanel(id) → SidePanelRegistry.Toggle(id)
    │ Sets ActiveLeftPanel, fires PropertyChanged
    │
    ▼
MainWindow.axaml.cs subscribes → UpdateLeftPanelContent(id)
    │ Switch on panelId → instantiate UserControl
    │ Set PanelHost.PanelContent + PanelHost.PanelActions
    │
    ▼
PanelHost (UserControl) renders:
    ┌─────────────────────────────┐
    │ [icon] Title    [actions] ≡ │  ← header (PanelActions slot)
    ├─────────────────────────────┤
    │                             │
    │   ContentPresenter          │  ← PanelContent
    │                             │
    └─────────────────────────────┘
```

### Visibility & Zen Mode

- `LeftSlotVisible` / `RightSlotVisible` → GridColumn widths (0 or 320/280)
- `BooleanToVisibilityConverter` hides PanelHost + ActivityBar
- **Zen mode:** `PanelService.EnterZenMode()` → all `*Visible = false` → `ZenEditorView` overlay (Panel.ZIndex=100)
- Layout restored from `layout.json` on next launch

### Registered Panels

#### Left Slot (sidebar)

| Id | Title | Icon | Order | View |
|----|-------|------|-------|------|
| `explorer` | Explorer | FolderOpen20 | 10 | `ExplorerPanel` → `ResourcesView` (tree/list of namespaces) |
| `source-code` | Source Code | Code20 | 20 | `SourceCodePanel` — scanned key usages, double-click opens file |
| `search` | Search | Search20 | 30 | `SearchPanel` — search box + filter buttons |
| `issues` | Issues | Warning20 | 35 | `IssuesPanel` — validation results, click navigates |
| `source-control` | Source Control | BranchFork20 | 40 | `SourceControlPanel` — placeholder for git integration |

#### Right Slot (inspector)

| Id | Title | Icon | Order | View |
|----|-------|------|-------|------|
| `languages` | Languages | LocalLanguage20 | 5 | `LanguagesPanel` → `LanguagesView` (per-language stats) |
| `inspector` | Inspector | Info20 | 10 | `InspectorPanel` → `InspectorView` (Suggestions, Details, Validation tabs) |
| `machine-translation` | Translation | Translate20 | 20 | `MachineTranslationPanel` — provider switcher + quick translate |
| `translation-memory` | Memory | Library20 | 30 | `TranslationMemoryPanel` — TM fuzzy matches |
| `dictionary` | Dictionary | Book20 | 40 | `DictionaryPanel` — placeholder |

---

## Status Bar System

### Design

Modular status bar with registerable panels, each with independent content, alignment, and click behavior.

### Type Hierarchy

```
IStatusBarPanel (Core contract)
    │   Id, Order, Alignment (Left/Center/Right), Content, Icon,
    │   Badge, BadgeSeverity, ClickCommand
    │
    └── Concrete panels in StatusBarPanels/BuiltInPanels.cs
```

### Components

| Type | Layer | Role |
|------|-------|------|
| `StatusBarPanelRegistry` | Core | Collects `IStatusBarPanel` implementations, exposes sorted collections |
| `StatusBarViewModel` | App | Singleton. Observable properties for all status fields |
| `StatusBarService` | App | Singleton bridge. Methods: `SetLoading`, `UpdateStatus`, `UpdateProjectName`, `UpdateCursor`, `UpdateDefaultLanguage`, `ShowNotificationBadge`, `UpdateSessionDirtyCount`, `UpdateSourceControl`, `UpdateStatistics` |
| `StatusBarView` | App | 32px footer. DataTemplates per panel type, Left/Right alignment |

### Built-in Panels

- **VcsPanel** — branch name + change count (left)
- **TranslationStatsPanel** — translated/total counts (left)
- **ModePanel** — current editor mode indicator (center)
- **ProjectPanel** — project name + path (right)
- **LanguagePanel** — default language code (right)
- **CursorPanel** — current key position (right)
- **NotificationPanel** — badge for warnings/errors (right)

---

## Load/Save Strategies

### Format Engine

The strategy pattern is the core extensibility point. Each format has a paired load+save identified by a string **format ID** (`FormatIds`; case-insensitive, stored in `toucan.tproj` as `saveFormat`). `TranslationStrategyFactory` resolves from DI-registered collections, which is also where plugin formats appear. The `SaveStyles` enum survives only as a compatibility map for the 14 built-ins and for migrating old project files (`saveStyle`).

A **save strategy owns the format's layout conventions**, so nothing else switches on the format: `DefaultFilePath(language)`, `LanguageFiles(root, language)`, `StoresCommentsInline` / `CommentSidecarBase`, `FileExtensions`, `DisplayName` and an optional `Detection` rule (`FormatDetection`) used by `FormatDetector`. Only `FormatId` and `DefaultFilePath` are mandatory; the rest have defaults.

### Load Strategies (14 + 1 manifest)

| Strategy | Format ID | Format Description |
|----------|-----------|-------------------|
| `JsonLoadStrategy` | `json` | Flat or nested JSON per-language file |
| `ManifestLoadStrategy` | `json` | Reads `toucan.tproj` manifest URLs (fallback to folder scan) |
| `NamespacedLoadStrategy` | `namespaced` | Delegates to JsonLoadStrategy; namespace handling differs on save |
| `YamlLoadStrategy` | `yaml` | YAML with indented hierarchy |
| `PoLoadStrategy` | `po` | PO/POT gettext files |
| `TomlLoadStrategy` | `toml` | TOML with `[section]` prefixes |
| `AndroidXmlLoadStrategy` | `android-xml` | `res/values-{lang}/strings.xml` |
| `IosStringsLoadStrategy` | `ios-strings` | `"key" = "value";` .strings files |
| `XliffLoadStrategy` | `xliff` | XLIFF 1.2 and 2.0 |
| `ArbLoadStrategy` | `arb` | Flutter ARB JSON |
| `CsvLoadStrategy` | `csv` | CSV (key,lang1,lang2… or key,language,value) |
| `ResxLoadStrategy` | `resx` | .NET .resx/.resw XML |
| `JavaPropertiesLoadStrategy` | `java-properties` | Java .properties files |
| `LaravelPhpLoadStrategy` | `laravel-php` | Laravel PHP array files |

### Save Strategies (14)

| Strategy | Format ID |
|----------|-----------|
| `JsonSaveStrategy` | `json` |
| `NamespacedSaveStrategy` | `namespaced` |
| `YamlSaveStrategy` | `yaml` |
| `PoSaveStrategy` | `po` |
| `IniSaveStrategy` | `ini` |
| `TomlSaveStrategy` | `toml` |
| `AndroidXmlSaveStrategy` | `android-xml` |
| `IosStringsSaveStrategy` | `ios-strings` |
| `XliffSaveStrategy` | `xliff` |
| `ArbSaveStrategy` | `arb` |
| `CsvSaveStrategy` | `csv` |
| `ResxSaveStrategy` | `resx` |
| `JavaPropertiesSaveStrategy` | `java-properties` |
| `LaravelPhpSaveStrategy` | `laravel-php` |

### Framework Profiles (auto-detection)

| Profile | Id | Default Format | Detection Logic |
|---------|----|----|---|
| `I18nextProfile` | i18next | json | Looks for `locales/` or `public/locales/` with JSON files |
| `AndroidProfile` | android | android-xml | Looks for `res/values*/strings.xml` |
| `FlutterArbProfile` | flutter | arb | Looks for `lib/l10n/` or `*.arb` files |
| `DotNetResxProfile` | dotnet | resx | Looks for `*.resx` or `Resources/` folder |
| `IosProfile` | ios | ios-strings | Looks for `*.lproj/Localizable.strings` |
| `RailsYamlProfile` | rails | yaml | Looks for `config/locales/*.yml` |
| `GettextProfile` | gettext | po | Looks for `*.po` or `locale/` with PO structure |
| `GenericJsonProfile` | generic-json | json | Fallback: any folder with JSON translation files |

---

## Validation Rules

| Rule | Id | Severity | Description |
|------|----|----------|-------------|
| `MissingTranslationRule` | `missing-translation` | Warning | Keys present in primary language but missing/empty in target |
| `PlaceholderMismatchRule` | `placeholder-mismatch` | Error | Placeholder count/pattern differs between source and target (`{{var}}`, `{0}`, `%s`, `:param`, `${var}`) |
| `DuplicateKeyRule` | `duplicate-key` | Error | Same (namespace, language) pair appears more than once |
| `UntranslatedCopyRule` | `untranslated-copy` | Info | Translation value identical to source language text |
| `EmptyValueRule` | `empty-value` | Warning | Keys with empty or whitespace-only values |
| `WhitespaceMismatchRule` | `whitespace-mismatch` | Info | Leading/trailing whitespace differs from source |

`ValidationPipeline` accepts `IEnumerable<IValidationRule>` via DI, so plugin rules join automatically. Exposes `RunAll()` and `Run(ruleIds)`. Executed on-save, on-demand via the Issues panel, and by `toucan check`. A rule reads `ValidationContext.Items` and `PrimaryLanguage`. The per-rule enable/severity switches in Settings cover only the six built-ins.

---

## Translation Providers

| Provider | API |
|----------|-----|
| `GoogleTranslationProvider` | Google Translate API |
| `DeepLTranslationProvider` | DeepL API (free + pro endpoints) |
| `MicrosoftTranslationProvider` | Microsoft Translator |
| `OpenAITranslationProvider` | OpenAI chat completions |
| `ClaudeTranslationProvider` | Anthropic Messages API (shares batching and parsing with Gemini in `LlmTranslationProvider`) |
| `GeminiTranslationProvider` | Google Gemini `generateContent` |
| `CustomWebhookTranslationProvider` | User-defined HTTP endpoint |
| `MockTranslationProvider` | Testing (prefixes value with `[MOCK]`) |

All implement `ITranslationProvider.PretranslateAsync(jobs, options, progress, ct)`. Placeholder patterns are stripped before API call and restored after via `PlaceholderService`.

Each provider exposes an optional `Definition` (`ProviderDefinition`: option and secret fields, defaults). `TranslationProviderRegistry` is built from the registered providers' definitions, so plugin providers appear in provider settings; the mock provider has none and stays unlisted.

---

## Configuration System

### ProjectSettings (`toucan.tproj`)

Per-project configuration persisted as JSON alongside translation files.

| Section | Fields |
|---------|--------|
| **Identity** | `Name`, `Description`, `Version` |
| **Languages** | `PrimaryLanguage`, `Languages` (list), `LanguageAliases`, `CustomFilePaths` |
| **Format/IO** | `SaveFormat` (string format ID; legacy `saveStyle` is migrated on load), `Framework`, `TranslationPackages` |
| **Editor** | `SaveEmptyTranslations`, `TranslationOrder`, `CopyTemplates` |
| **Provider** | `DefaultProvider`, `Context`, `Formality` |
| **Source Code** | `SourceRoots`, `ExternalEditorCommand` |
| **Auto-Save** | `AutoSaveEnabled`, `AutoSaveIntervalSeconds` |
| **UI State** | `HiddenNamespaces` |

### AppOptions (`~/Documents/Toucan/settings.json`)

Global user preferences, independent of any project.

| Section | Fields |
|---------|--------|
| **Appearance** | `Theme` (Light/Dark/System), `Language` (UI locale), `AccentColor` |
| **Editor** | `DefaultLanguage`, `PageSize`, `InfiniteScroll`, `ShowSuggestions` |
| **General** | `CheckForUpdates`, `OpenLastProject`, `RecentProjectsMax` |
| **Providers** | `DefaultProviderId` (app-level fallback) |

Loaded via `IPreferenceService`. Provider API keys stored separately via `IProviderSettingsService`, encrypted by `ISecureStorageService` (DPAPI on Windows; AES-GCM on macOS/Linux in the Avalonia app).

### Plugin files

| Path | Purpose |
|------|---------|
| `~/Documents/Toucan/plugins/<id>/` | Installed plugins (`plugin.json` + assemblies). Override with `TOUCAN_PLUGINS_DIR` (CLI). |
| `~/Documents/Toucan/plugin-policy.json` | Enabled state, trusted content hashes and dismissed prompts, shared by the app and the CLI. Override with `TOUCAN_PLUGIN_POLICY` (CLI). |

### Layout State (`~/Documents/Toucan/layout.json`)

Persisted by `PanelService`:
- Active panel IDs (left + right)
- Panel visibility flags
- Column widths
- Editor mode
- Zen mode state

---

## Directory Map

```
toucan/                              Repository root
├── ToucanProject.slnx               Full solution
├── Toucan.CrossPlatform.slnx        Same projects, without the x86/ARM platform mappings
│
├── Toucan.Core/                     Core library (platform-agnostic)
│   ├── Contracts/                   Interface definitions
│   │   └── Services/               Service-layer interfaces
│   ├── Models/                      Domain models and DTOs
│   ├── Options/                     AppOptions (global prefs)
│   ├── Helpers/                     JsonHelper, streaming parser
│   ├── Plugins/                     PluginHost, load context, trust policy, hasher, signature seam
│   ├── ToucanCoreServiceCollectionExtensions.cs   AddToucanCore() composition root
│   └── Services/                    All implementations
│       ├── Frameworks/              8 IFrameworkProfile implementations
│       ├── LoadStrategies/          14 ILoadStrategy implementations
│       ├── SaveStrategies/          14 ISaveStrategy implementations
│       ├── Providers/               6 ITranslationProvider implementations
│       └── Validation/              ValidationPipeline + 6 rules
│
├── Toucan.Plugins.Abstractions/     Plugin contract (also published as a NuGet package)
│   ├── Contracts/                   ISaveStrategy, ILoadStrategy, ITranslationProvider, IValidationRule, IFrameworkProfile
│   ├── Models/                      TranslationItem, FormatIds, ProviderDefinition, FormatDetection, …
│   └── Plugin/                      IToucanPlugin, IPluginContext, PluginManifest, PluginApi
│
├── samples/Toucan.Sample.Plugin/    Complete example plugin (TSV format + rule)
│
├── Toucan.CLI/                      Command-line tool
│   └── Program.cs                   Entry point + all commands
│
├── Toucan.Avalonia/                 Cross-platform desktop app
│   ├── ViewModels/                  Avalonia-specific VMs
│   ├── Views/                       Avalonia XAML views + dialogs
│   └── Services/                    Platform service implementations
│
├── tests/Toucan.Core.Tests/         Core unit tests (xUnit v3, FsCheck, NSubstitute): formats, composition root, plugin host/trust
├── tests/Toucan.Avalonia.Tests/     Headless Avalonia UI and view-model tests
├── tests/Plugins/Toucan.TestPlugins/  Fixture plugins (well-behaved and misbehaving) for the host tests
│
├── Tools/
│   ├── Babel2Toucan.cs             .babel → toucan.project converter (C#)
│   └── babel2toucan.py             Same converter (Python)
│
└── docs/
    ├── ARCHITECTURE.md              ← this file
    ├── plugins.md                   Plugin author guide
    ├── completed-features.md        Shipped features (v0.19.0 Avalonia on all platforms)
    ├── known-bugs.md                Active bug tracker
    ├── branding.md                  Brand guidelines
    ├── provider-settings.md         Provider configuration docs
    ├── pretranslation-preview.md    Dry-run/preview feature docs
    ├── visual-review.md             Headless screenshots of every screen
    ├── INDEX.md                     Generated list of every doc with status, progress, summary
    ├── toucan.project.schema.json   JSON Schema for project files
    ├── index.html                   Website (toucan.rasyid.dev), self-contained HTML
    ├── todos/
    │   └── future-roadmap.md        Roadmap: v0.20 → v1.0, then post-1.0 plans
    ├── research/
    │   ├── babel-format-reference.md
    │   └── toucan-project-schema.md
    ├── archive/                     Finished or deprecated docs, kept for history (see the doc-status skill)
    │   ├── plugin-system-plan.md, panel-extension-plan.md, Core-Modularization-Plan.md
    │   └── UI-Revamp-Plan.md, ui-polish-plan-deprecated.md, wpf-parity.md
    ├── specs/                       Kiro specs (requirements, design, tasks), moved from .kiro/specs
    └── test-project/                Sample translation files
```

---

## Internal Model

All formats normalize to a flat list of `TranslationItem`:

```csharp
{ Language: "fr-FR", Namespace: "app.dialog.save_button", Value: "Sauvegarder" }
```

- **Language** — BCP-47 locale code
- **Namespace** — dot-separated key path (hierarchical keys flattened with `.`)
- **Value** — the translated string

This is the canonical interchange format. Every load strategy produces it, every save strategy consumes it. See `Toucan.Core/ARCHITECTURE.md` for the format interop design.

---

## Editor Modes

| Mode | Purpose | Behavior |
|------|---------|----------|
| **Editor** | Normal translation work | Full editing, suggestions, TM |
| **Review** | Quality review | Auto-filters unapproved items, approve/reject buttons prominent |
| **Audit** | Read-only inspection | No editing, shows approval state + change history |

Selected via `ModeSelectorBar` in the title bar (`TitleBar.TrailingContent`). Persisted in layout state.

---

## Plugin System

Plugins are .NET assemblies in `Documents/Toucan/plugins/<id>/` with a `plugin.json` manifest. Authors reference the
`Toucan.Plugins.Abstractions` package; the guide is [plugins.md](plugins.md), the design history is
[archive/plugin-system-plan.md](archive/plugin-system-plan.md).

```
startup
  AddLogging → AddToucanCore() → AddToucanPlugins(options)  → BuildServiceProvider
                                      │
                    PluginHost.LoadInto(services)
                      for each <plugins>/<folder>/plugin.json (name order):
                        parse + validate manifest        → Rejected
                        duplicate plugin id              → Rejected
                        disabled (policy)                → Disabled
                        plugin API compatible?           → Rejected
                        SHA-256 of the folder, signature → Rejected if signature invalid
                        trusted (policy, exact hash)?    → NeedsTrust (no code runs)
                        load in own AssemblyLoadContext, find IToucanPlugin, Initialize(context)
                        validate registrations (capabilities, ID collisions, format rules)
                        commit → services.AddSingleton(...)   │  any failure → Failed, nothing applied
                                      │
                         IPluginCatalog (every plugin found, loaded or not)
```

- **Isolation:** one non-collectible `AssemblyLoadContext` per plugin; `Toucan.Plugins.Abstractions`, `Toucan.Core` and the `Microsoft.Extensions` abstractions always resolve from the host so contract types are shared.
- **All or nothing:** a plugin's registrations are applied only if `Initialize` returns and every registration is valid. IDs of formats, providers, rules and profiles are reserved against the built-ins (read from a throwaway container) and earlier plugins.
- **Trust:** `IPluginPolicy` / `FilePluginPolicyStore` (`Documents/Toucan/plugin-policy.json`) records enabled state and trusted content hashes; `IPluginSignatureVerifier` is a stub that reports everything "not signed" (signing becomes mandatory with the collaboration/auth milestone).
- **Hosts:** Avalonia (Settings → Plugins, startup prompt) and the CLI (`toucan plugins …`, never prompts).  Changes need a restart; plugins are never unloaded.
- **Missing plugin:** a project whose format has no strategy fails to open with `FormatUnavailableException` instead of falling back to JSON.

---

## Key Architectural Patterns

| Pattern | Where | Purpose |
|---------|-------|---------|
| Strategy | Load/Save strategies | Format extensibility without modifying core; strategies also own layout conventions |
| Plugin host | `PluginHost` | Load third-party assemblies in isolated contexts behind a trust gate |
| Composition root | `AddToucanCore()` | One registration path shared by the GUI, the CLI and plugins |
| Registry | SidePanelRegistry, StatusBarPanelRegistry, TranslationProviderRegistry | Dynamic UI composition from DI |
| Lifecycle service | IProjectLifecycleService | Orchestrates open/save/close with guards |
| Baseline diffing | TranslationManagementService | Dirty tracking without filesystem reads |
| Three-way merge | IDiffMergeEngine | External change reconciliation |
| Sidecar files | CommentPersistence, AuditService | Metadata for formats that don't support inline comments |
| Partial classes | MainWindowViewModel (5 files) | Large VM decomposition without inheritance |

---

## Adding New Functionality

### New Format
1. Create `XxxLoadStrategy : ILoadStrategy` in `Services/LoadStrategies/` and `XxxSaveStrategy : ISaveStrategy` in `Services/SaveStrategies/`, both returning the same `FormatId`; add the ID as a constant in `FormatIds` (and a `SaveStyles` map entry only if it is a built-in).
2. On the save strategy implement `DefaultFilePath`, and as needed `DisplayName`, `FileExtensions`, `LanguageFiles`, `StoresCommentsInline` and `Detection`.
3. Register both in `ToucanCoreServiceCollectionExtensions.AddToucanFormats()` (and add the save strategy to `BuiltInFormats`; a test keeps the two lists in step).
4. Optionally add an `IFrameworkProfile` for auto-detection.

To ship a format *outside* this repository, write a plugin instead (see [plugins.md](plugins.md)).

### New Panel
1. Create `ISidePanel` implementation (or use `BuiltInSidePanel`)
2. Register in `SidePanelRegistry` during app startup
3. Create corresponding `UserControl` (panel content view)
4. Add case to `UpdateLeftPanelContent` / `UpdateRightPanelContent` in `MainWindow.axaml.cs`

### New Validation Rule
1. Implement `IValidationRule` in `Services/Validation/`
2. Register in `AddToucanValidation()` — `ValidationPipeline` picks it up automatically
3. Add it to the Validation settings list (`OptionsViewModel`) and the defaults in `ProjectDefaults` if it should have enable/severity switches (plugin rules do not have these yet)

### New Translation Provider
1. Implement `ITranslationProvider` and set its `Definition` (a `ProviderDefinition`: option/secret fields and defaults for the settings UI)
2. Register in `AddToucanProviders()` — `TranslationProviderRegistry` is built from the registered providers, so it is listed automatically

### Plugin
See [plugins.md](plugins.md). Plugins register formats, providers, rules and profiles through `IPluginContext`; the host applies them to the same container.
