# Toucan — Architecture

> Last updated: 2026-07-03

---

## High-Level Dependency Graph

```
┌─────────────────────────────────────────────────────────────────┐
│                         Consumers                                │
├──────────────┬──────────────────────┬───────────────────────────┤
│  Toucan      │  Toucan.CLI          │  Toucan.Avalonia          │
│  (WPF App)   │  (Console)           │  (Cross-Platform)         │
│  .NET 10     │  .NET 10             │  .NET 10                  │
│  WPF-UI 4.3  │  No UI deps          │  Avalonia 12.0.5          │
│  Ookii.Dlgs  │                      │  FluentAvaloniaUI 3.0     │
│  MS.Ext.DI   │                      │  MS.Ext.DI               │
│  MS.Ext.Log  │                      │                           │
└──────┬───────┴──────────┬───────────┴───────────┬───────────────┘
       │                  │                       │
       └──────────────────┼───────────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │     Toucan.Core       │
              │     .NET 10           │
              │                       │
              │  CommunityToolkit.Mvvm│
              │  ClosedXML            │
              │  MS.Ext.Logging.Abs   │
              └───────────────────────┘
```

All three consumer projects reference `Toucan.Core`. The core has no UI dependencies and no DI container — consumers compose services themselves.

---

## Layer Overview

### Toucan.Core

Platform-agnostic library containing all business logic:

- **Models** — `TranslationItem`, `NsTreeItem`, `ProjectSettings`, `SaveStyles`, `SidePanel`, `StatusBarPanel`, and DTOs
- **Contracts** — interfaces for every service (`ILoadStrategy`, `ISaveStrategy`, `IProjectLifecycleService`, `ITranslationProvider`, etc.)
- **Services** — implementations: strategy factory, project lifecycle, translation management, validation, TM, audit, auto-save, fuzzy search
- **Format Engine** — 14 load strategies + 14 save strategies (strategy pattern keyed by `SaveStyles` enum)
- **Framework Profiles** — 8 auto-detection profiles (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, Generic JSON)
- **Translation Providers** — Google, DeepL, Microsoft, OpenAI, Custom Webhook, Mock
- **Validation** — 6 rules + pipeline

**Dependencies (3):** CommunityToolkit.Mvvm 8.4.2, ClosedXML 0.105.0, Microsoft.Extensions.Logging.Abstractions 10.0.9

### Toucan (WPF App)

Primary desktop client — Windows-only, Fluent Design (Mica backdrop, system theme).

- **MVVM** via CommunityToolkit.Mvvm source generators
- **MainWindowViewModel** split into 5 partial files: base state, navigation, file ops, edit ops, translation ops
- **Panel system** — VS Code-style left/right activity bars with dynamic panel content
- **Services** — PanelService (layout), KeybindingService (shortcuts), DialogService, StatusBarService, FileAssociationService
- **Views** — Components (reusable controls), Panels (sidebar content), Dialogs (modals), Settings (options pages)

**Dependencies:** WPF-UI 4.3.0, Ookii.Dialogs.Wpf 5.0.1, Microsoft.Extensions.DependencyInjection 10.0.9, Microsoft.Extensions.Logging 10.0.9

### Toucan.CLI

Headless console tool for CI/CD integration.

- **8 commands:** `check`, `stats`, `translate`, `export`, `list-formats`, `list-keys`, `get`, `set`
- Manual service composition (no DI container) — constructs strategies and services directly
- Outputs: color console (progress bars), JSON (get), exit codes for CI

**Dependencies:** Toucan.Core only

### Toucan.Avalonia

Cross-platform desktop client (~80% feature parity with WPF).

- **Framework:** Avalonia 12.0.5 + FluentAvaloniaUI 3.0.0
- **State:** Functional early port — all core workflows work, advanced features (zen mode, review/audit modes, source code panel, TM panel) deferred
- Full DI setup mirroring WPF app
- 12 ViewModels, 7 platform services, dialogs via Avalonia StorageProvider

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
    │   └─► select SaveStyle from winning profile
    │
    └─► ConfigManifest: read toucan.tproj → get SaveStyle + package URLs
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
    └── BuiltInSidePanel (WPF App) — concrete registration instances
```

### Registry & Service

| Type | Layer | Role |
|------|-------|------|
| `SidePanelRegistry` | Core | Singleton. Register/Unregister/Activate/Toggle. Tracks `ActiveLeftPanel` + `ActiveRightPanel`. Fires `PropertyChanged`. |
| `PanelService` | WPF | Singleton. Wraps registry. Adds toggle commands, zen mode, layout persistence (`~/Documents/Toucan/layout.json`). |

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
MainWindow.xaml.cs subscribes → UpdateLeftPanelContent(id)
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
| `StatusBarViewModel` | WPF | Singleton. Observable properties for all status fields |
| `StatusBarService` | WPF | Singleton bridge. Methods: `SetLoading`, `UpdateStatus`, `UpdateProjectName`, `UpdateCursor`, `UpdateDefaultLanguage`, `ShowNotificationBadge`, `UpdateSessionDirtyCount`, `UpdateSourceControl`, `UpdateStatistics` |
| `StatusBarView` | WPF | 32px footer. DataTemplates per panel type, Left/Right alignment |

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

The strategy pattern is the core extensibility point. Each format has a paired load+save keyed by `SaveStyles` enum. `TranslationStrategyFactory` resolves from DI-registered collections.

### Load Strategies (14 + 1 manifest)

| Strategy | SaveStyles | Format Description |
|----------|------------|-------------------|
| `JsonLoadStrategy` | `Json` | Flat or nested JSON per-language file |
| `ManifestLoadStrategy` | `Json` | Reads `toucan.tproj` manifest URLs (fallback to folder scan) |
| `NamespacedLoadStrategy` | `Namespaced` | Delegates to JsonLoadStrategy; namespace handling differs on save |
| `YamlLoadStrategy` | `Yaml` | YAML with indented hierarchy |
| `PoLoadStrategy` | `Properties` | PO/POT gettext files |
| `TomlLoadStrategy` | `Toml` | TOML with `[section]` prefixes |
| `AndroidXmlLoadStrategy` | `AndroidXml` | `res/values-{lang}/strings.xml` |
| `IosStringsLoadStrategy` | `IosStrings` | `"key" = "value";` .strings files |
| `XliffLoadStrategy` | `Xliff` | XLIFF 1.2 and 2.0 |
| `ArbLoadStrategy` | `Arb` | Flutter ARB JSON |
| `CsvLoadStrategy` | `Csv` | CSV (key,lang1,lang2… or key,language,value) |
| `ResxLoadStrategy` | `Resx` | .NET .resx/.resw XML |
| `JavaPropertiesLoadStrategy` | `JavaProperties` | Java .properties files |
| `LaravelPhpLoadStrategy` | `LaravelPhp` | Laravel PHP array files |

### Save Strategies (14)

| Strategy | SaveStyles |
|----------|------------|
| `JsonSaveStrategy` | `Json` |
| `NamespacedSaveStrategy` | `Namespaced` |
| `YamlSaveStrategy` | `Yaml` |
| `PoSaveStrategy` | `Properties` |
| `IniSaveStrategy` | `Adb` |
| `TomlSaveStrategy` | `Toml` |
| `AndroidXmlSaveStrategy` | `AndroidXml` |
| `IosStringsSaveStrategy` | `IosStrings` |
| `XliffSaveStrategy` | `Xliff` |
| `ArbSaveStrategy` | `Arb` |
| `CsvSaveStrategy` | `Csv` |
| `ResxSaveStrategy` | `Resx` |
| `JavaPropertiesSaveStrategy` | `JavaProperties` |
| `LaravelPhpSaveStrategy` | `LaravelPhp` |

### Framework Profiles (auto-detection)

| Profile | Id | Default Format | Detection Logic |
|---------|----|----|---|
| `I18nextProfile` | i18next | Json | Looks for `locales/` or `public/locales/` with JSON files |
| `AndroidProfile` | android | AndroidXml | Looks for `res/values*/strings.xml` |
| `FlutterArbProfile` | flutter | Arb | Looks for `lib/l10n/` or `*.arb` files |
| `DotNetResxProfile` | dotnet | Resx | Looks for `*.resx` or `Resources/` folder |
| `IosProfile` | ios | IosStrings | Looks for `*.lproj/Localizable.strings` |
| `RailsYamlProfile` | rails | Yaml | Looks for `config/locales/*.yml` |
| `GettextProfile` | gettext | Properties | Looks for `*.po` or `locale/` with PO structure |
| `GenericJsonProfile` | generic-json | Json | Fallback: any folder with JSON translation files |

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

`ValidationPipeline` accepts `IEnumerable<IValidationRule>` via DI. Exposes `RunAll()` and `Run(ruleIds)`. Executed on-save and on-demand via the Issues panel.

---

## Translation Providers

| Provider | API |
|----------|-----|
| `GoogleTranslationProvider` | Google Translate API |
| `DeepLTranslationProvider` | DeepL API (free + pro endpoints) |
| `MicrosoftTranslationProvider` | Microsoft Translator |
| `OpenAITranslationProvider` | OpenAI chat completions |
| `CustomWebhookTranslationProvider` | User-defined HTTP endpoint |
| `MockTranslationProvider` | Testing (prefixes value with `[MOCK]`) |

All implement `ITranslationProvider.PretranslateAsync(jobs, options, progress, ct)`. Placeholder patterns are stripped before API call and restored after via `PlaceholderService`.

---

## Configuration System

### ProjectSettings (`toucan.tproj`)

Per-project configuration persisted as JSON alongside translation files.

| Section | Fields |
|---------|--------|
| **Identity** | `Name`, `Description`, `Version` |
| **Languages** | `PrimaryLanguage`, `Languages` (list), `LanguageAliases`, `CustomFilePaths` |
| **Format/IO** | `SaveStyle` (enum), `Framework`, `TranslationPackages` |
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

Loaded via `IPreferenceService`. Provider API keys stored separately via `IProviderSettingsService` with DPAPI encryption (`ISecureStorageService`).

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
Toucan/                              Solution root
├── Toucan.sln                       Solution file
│
├── Toucan.Core/                     Core library (platform-agnostic)
│   ├── Contracts/                   Interface definitions
│   │   └── Services/               Service-layer interfaces
│   ├── Models/                      Domain models and DTOs
│   ├── Options/                     AppOptions (global prefs)
│   ├── Extensions/                  Extension methods
│   ├── Helpers/                     JsonHelper, streaming parser
│   └── Services/                    All implementations
│       ├── Frameworks/              8 IFrameworkProfile implementations
│       ├── LoadStrategies/          14 ILoadStrategy implementations
│       ├── SaveStrategies/          14 ISaveStrategy implementations
│       ├── Providers/               6 ITranslationProvider implementations
│       └── Validation/              ValidationPipeline + 6 rules
│
├── Toucan/                          WPF desktop app (primary)
│   ├── ViewModels/                  MVVM ViewModels
│   │   └── StatusBarPanels/        Built-in status bar panel definitions
│   ├── Services/                    UI-layer services (Panel, Keybinding, Dialog, etc.)
│   ├── Views/
│   │   ├── Components/             Reusable UI controls (ActivityBar, PanelHost, etc.)
│   │   ├── Panels/                 Side panel content (Explorer, Search, Issues, etc.)
│   │   ├── Dialogs/                Modal windows
│   │   ├── Settings/               Options dialog pages
│   │   └── NewProject/             New project wizard steps
│   ├── Converters/                  WPF value converters
│   ├── Locales/                     Resx-based UI strings (en + id-ID)
│   ├── Resources/                   DesignTokens.xaml, merged dictionaries
│   └── Assets/                      Images, icons, splash screen
│
├── Toucan.CLI/                      Command-line tool
│   └── Program.cs                   Entry point + all commands
│
├── Toucan.Avalonia/                 Cross-platform desktop app
│   ├── ViewModels/                  Avalonia-specific VMs
│   ├── Views/                       Avalonia XAML views + dialogs
│   └── Services/                    Platform service implementations
│
├── Toucan.Core.Tests/               Core unit tests (xUnit v3, FsCheck, NSubstitute)
│   └── IO/Generators/              Property-based test generators
│
├── Toucan.Tests/                    WPF ViewModel tests (xUnit v3)
│
├── Tools/
│   ├── Babel2Toucan.cs             .babel → toucan.project converter (C#)
│   └── babel2toucan.py             Same converter (Python)
│
└── docs/
    ├── ARCHITECTURE.md              ← this file
    ├── completed-features.md        All 88 v1.0 features
    ├── known-bugs.md                Active bug tracker
    ├── ui-revamp-plan.md            UI redesign plan
    ├── branding.md                  Brand guidelines
    ├── provider-settings.md         Provider configuration docs
    ├── pretranslation-preview.md    Dry-run/preview feature docs
    ├── roadmap.json                 Machine-readable roadmap
    ├── toucan.project.schema.json   JSON Schema for project files
    ├── index.html                   GitHub Pages landing page
    ├── todos/
    │   ├── future-roadmap.md        Post-1.0 roadmap (v1.1–v2.0)
    │   ├── panel-extension-plan.md  Inspector panel extension plan
    │   └── ui-polish-plan.md        UI polish items
    ├── research/
    │   ├── babel-format-reference.md
    │   ├── toucan-project-schema.md
    │   └── core-modularization-plan.md
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

## Key Architectural Patterns

| Pattern | Where | Purpose |
|---------|-------|---------|
| Strategy | Load/Save strategies | Format extensibility without modifying core |
| Registry | SidePanelRegistry, StatusBarPanelRegistry, TranslationProviderRegistry | Dynamic UI composition from DI |
| Lifecycle service | IProjectLifecycleService | Orchestrates open/save/close with guards |
| Baseline diffing | TranslationManagementService | Dirty tracking without filesystem reads |
| Three-way merge | IDiffMergeEngine | External change reconciliation |
| Sidecar files | CommentPersistence, AuditService | Metadata for formats that don't support inline comments |
| Partial classes | MainWindowViewModel (5 files) | Large VM decomposition without inheritance |

---

## Adding New Functionality

### New Format
1. Add value to `SaveStyles` enum
2. Create `XxxLoadStrategy : ILoadStrategy` in `Services/LoadStrategies/`
3. Create `XxxSaveStrategy : ISaveStrategy` in `Services/SaveStrategies/`
4. Register both in DI (App.xaml.cs / App.axaml.cs / CLI Program.cs)
5. Optionally: add `IFrameworkProfile` for auto-detection

### New Panel
1. Create `ISidePanel` implementation (or use `BuiltInSidePanel`)
2. Register in `SidePanelRegistry` during app startup
3. Create corresponding `UserControl` (panel content view)
4. Add case to `UpdateLeftPanelContent` / `UpdateRightPanelContent` in MainWindow.xaml.cs

### New Validation Rule
1. Implement `IValidationRule` in `Services/Validation/`
2. Register in DI — `ValidationPipeline` picks it up automatically

### New Translation Provider
1. Implement `ITranslationProvider`
2. Create `ProviderDefinition` (schema for settings UI)
3. Register in DI — `TranslationProviderRegistry` discovers it
