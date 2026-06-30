 4jj  ./;'l;p#';;lllllll';//
   '
   ;?:>
   /"Changelog

All notable changes to this project will be documented in this file.

## [0.13.0] — 2026-06-30

### Added
- **App i18n infrastructure** — `Toucan.Locales.Strings` resource class with `ResourceManager`-backed localization
- **Strings.resx** (en-US) — 45+ key UI strings extracted: menus, panels, tooltips, status messages, mode labels
- **Strings.id-ID.resx** — Full Indonesian translation of all UI strings
- **App language picker** — ComboBox in Settings → Options → General for selecting UI language
- **AppLanguage option** — Persisted to `settings.json`, applied as `CurrentUICulture` on startup

### Changed
- App sets `CurrentUICulture` on startup from saved language preference (requires restart for full effect)

## [0.12.0] — 2026-06-30

### Added
- **Theme switcher** — Light/Dark/System theme selection in Settings → Options
- Theme persisted to `settings.json` and applied on startup via `ApplicationThemeManager`
- Theme applied immediately when changed in Options (no restart needed)

## [0.11.0] — 2026-06-30

### Added
- **Splash screen** — Shows `splash.png` immediately on startup before DI initialization
- **Inspector panel: Suggestions tab** — Wired to show similar translations when a key is selected
- **Inspector panel: Details tab** — Shows key ID, comment, language statuses, and audit metadata
- **Translation ID filter** — Local filter TextBox in the editor panel for filtering translation keys
- **Plural key grouping** — Plural variants (`_one`, `_other`, `_zero`, etc.) merged into a single visual card
- **Mode selector pill style** — Segmented control replaces circular radio buttons for Edit/Review/Audit
- **TreeView virtualization** — Enabled `VirtualizingStackPanel` with recycling for large key trees
- **View menu toggles** — Added Toggle Sidebar, Inspector, Toolbar, and Status Bar to Views menu
- **Panel column collapse** — Sidebar and Inspector columns collapse to zero width when hidden

### Fixed
- **Default language ComboBox** — Was passing `ComboBoxItem` object instead of language string (added `SelectedValuePath="Content"`)
- **App crash on startup** — `AssemblyInfo.cs` version mismatch with csproj caused silent `FileNotFoundException`
- **Language binding crash** — Removed circular `FrameworkElement.Language` binding that caused `XamlParseException`
- **Sidebar not collapsing** — Column width now set via code-behind (WPF `ColumnDefinition` ignores Style triggers)
- **Startup layout** — Saved panel visibility state now applied to column widths on launch

### Changed
- **Toolbar** — Removed redundant Search TextBox (filter in editor panel replaces it); Open button restyled as split button with recent projects flyout
- **Memory leaks fixed** — Reused DispatcherTimers in `OnSearchTextChanged` and `RefreshTree`; `TranslationItemViewModel` implements `IDisposable`; `LanguageGroupViewModel.LoadTranslations` disposes old items; `MainWindow.Closing` unsubscribes events and disposes resources
- **Show All button** — Restyled as flat button with accent color and hover effect
- **PaginationControl** — Status bar row index fixed after filter TextBox addition

### Removed
- **Toolbar Search TextBox** — Replaced by in-panel "Filter translation IDs" input
- **Unused `ViewAsProperty`** — Dead dependency property removed from `TranslationDetailsView`

## [0.9.0] — 2026-06-28

### Added
- **FuzzySearchService** — Hybrid search engine combining exact, prefix, substring, and trigram-based Jaccard similarity matching against both translation keys and values
- **IFuzzySearchService** interface — Contract for the fuzzy search engine with `Search()` and `ComputeTrigramSimilarity()` methods
- **SearchMatch record** — Represents a search result with item, score, and match type
- **SearchMatchType enum** — Classifies matches as Exact (1.0), Prefix (0.9), Contains (0.7), or Fuzzy (Jaccard score)
- **Search TextBox in toolbar** — Right-aligned search input with Fluent styling, bound with `UpdateSourceTrigger=PropertyChanged`
- **Search debounce** — 300ms DispatcherTimer-based debounce prevents excessive filtering during typing
- **Result count indicator** — Status bar shows "Showing X of Y items" when a filter is active

### Changed
- **Search bar relocated** — Moved from window title bar to toolbar row for better accessibility
- **Search logic** — Replaced namespace-only prefix filtering with full fuzzy search across keys and values
- **MainWindowViewModel.Nav.cs** — `Search()` method now delegates to `IFuzzySearchService` with graceful fallback
- **App.xaml.cs** — Registered `FuzzySearchService` as singleton in DI container

### Removed
- **SearchFilterTextbox from TitleBar** — Search input no longer lives in `TitleBar.TrailingContent`
- **SearchFilterTextbox_PreviewKeyDown handler** — Enter-key binding no longer needed with PropertyChanged trigger

## [0.8.0] — 2026-06-28

### Added
- **IProjectLifecycleService** — Unified open/save/close/save-as orchestration; all entry points (folder picker, file picker, recent, new project) funnel through a single load pipeline
- **ITranslationManagementService** — In-memory translation collection with per-item dirty tracking via baseline comparison and 500ms debounce
- **ILanguageManagementService** — Language add/remove/reorder with disk cleanup and manifest sync
- **IAutoSaveService** — Timer-based periodic persistence with interval clamping (10–600s), SemaphoreSlim concurrency guard
- **IDiffMergeEngine** — Three-way diff (base/mine/theirs) with categorization (Added/Modified/Deleted/Conflicting) and non-conflicting auto-merge
- **IAuditService** — Per-item audit metadata (LastModified, Approved, ChangeType) with `.toucan-metadata.json` sidecar persistence
- **ICommentPersistenceService** — Comment sidecar read/write for formats without inline comment support; 2000-char truncation, orphan discard
- **IUnsavedChangesHandler** — UI-agnostic callback for Save/Discard/Cancel prompt on close
- **IExternalChangeHandler** — UI-agnostic callback for Reload/Merge/Ignore prompt on external file changes
- **TranslationBaseline model** — Stores last-saved value/comment per item for dirty detection
- **EditAction model** — Extracted from UndoRedoService into Toucan.Core for cross-project use
- **FsCheck generators** — TranslationItemGenerator, EditSequenceGenerator, DiffTripleGenerator for property-based testing
- **CommentPersistenceServiceTests** — 30 unit tests covering sidecar round-trip, truncation, orphan handling

### Changed
- **UndoRedoService** — Refactored from static singleton to DI-registered `IUndoRedoService` interface
- **FileWatcherService** — Refactored behind `IFileWatcherService` interface; changed event signature to `EventHandler`
- **TranslationItem** — Extended with `LastModifiedUtc`, `ApprovedAtUtc`, `ChangeType` audit properties
- **ProjectSettings** — Added `AutoSaveEnabled` and `AutoSaveIntervalSeconds` configuration properties
- **App.xaml.cs** — All new services registered in DI container with factory patterns for circular dependencies
- **MainWindowViewModel.File.cs** — Delegates open/save/close to IProjectLifecycleService; retains only RelayCommands and UI bindings
- **MainWindowViewModel.Translation.cs** — Delegates dirty tracking to ITranslationManagementService; subscribes to DirtyStateChanged
- **MainWindowViewModel.Edit.cs** — Delegates language operations to ILanguageManagementService; retains confirmation dialogs
- **TranslationItemViewModel** — Receives IUndoRedoService via DI instead of static reference
- **Toucan.Core.Tests.csproj** — Added FsCheck 3.3.2, NSubstitute 5.3.0, System.IO.Abstractions 22.1.1

## [0.7.0] — 2026-06-27

### Added
- **2-step New Project wizard** — Step 1: framework + title + folder, Step 2: language manager
- **WinUI sidebar preferences** — Settings dialog with 7-page sidebar navigation
- **Suggested languages management** — configurable in Preferences → Languages
- **Default project name** — auto-generated from selected framework

### Fixed
- **BAML runtime error** — window icon set via pack URI in code-behind
- **KeyGesture crash** — bare J/K keys moved to PreviewKeyDown handler
- **Start screen overlapping** — toolbar/panels hidden when start screen visible
- **Start screen icon** — replaced globe emoji with logo image
- **Recent projects empty names** — `Project.Name` trims trailing slashes
- **Start screen buttons** — bound to correct MainWindowViewModel commands
- **Recent projects list** — uses `Project.Name`/`Path` with `OpenRecentProjectCommand`
- **Statistics dialog** — ProgressBar binding set to OneWay
- **Recent menu style crash** — TargetType fixed to `{x:Type MenuItem}`
- **Preferences null reference** — guard in `NavList_SelectionChanged` during init
- **New Project StaticResource error** — BoolToVis converter moved before first usage
- **Dialog button standardization** — consistent footer pattern across all dialogs

## [0.7.0] — 2026-06-27

### Added
- **Framework Profiles** — New `IFrameworkProfile` abstraction separating file format from project conventions
  - 8 profiles: Generic JSON, i18next, Android, Flutter ARB, .NET RESX, iOS, Gettext, Rails YAML
  - Auto-detection via `DetectionScore()` — highest-scoring profile wins
  - File discovery, language extraction from paths, output path generation
- **Validation Pipeline** — Pre-save verification with 6 built-in rules:
  - Missing translations (keys in primary but empty in targets)
  - Placeholder mismatch (different `{0}`, `{{var}}`, `%s` counts)
  - Duplicate keys (same namespace + language)
  - Untranslated copies (value identical to source)
  - Empty values (blank strings)
  - Whitespace mismatch (leading/trailing differences)
- **Import Project dialog** — Auto-detects framework from existing folder, shows discovered files and languages, user can override detection
- **Custom Webhook provider** — POST translations to any HTTP endpoint with configurable auth
- **GitHub Pages documentation** — Vue-rendered roadmap with Lucide icons and Mermaid diagrams
- **ProjectSettings.Framework** field — Persists detected/selected framework in manifest

### Fixed
- **Critical: Socket exhaustion** — All translation providers now use a shared static `HttpClient` instead of creating one per call
- **Critical: Cross-thread UI access** — `TranslationItemViewModel` debounce timer replaced with `DispatcherTimer` (fires on UI thread)
- **High: JsonDocument memory leak** — `JsonParser.Parse` now disposes `JsonDocument` in `finally` block even if enumeration is abandoned
- **High: PreTranslate race condition** — Added `IsRunning` guard to prevent double-invocation of Start command
- **High: CancellationToken not passed** — Google and DeepL providers now pass cancellation token to HTTP calls
- **High: FileService null returns** — `ReadText`/`ReadBytes` return empty values instead of `null!`
- **High: ProjectService.Save crash** — Alias reverse mapping no longer throws on duplicate language codes
- **Medium: Duplicate DI registrations** — Removed duplicate `ISecureStorageService`/`IProviderSettingsService` registrations
- **Medium: IsDirty after load** — Project no longer marked dirty immediately after opening
- **Medium: Division by zero** — `BulkActionService.GenerateStatistics` and `SummaryItem.Percentage` guard against zero totals
- **Medium: MessageService crash** — Null-safe `MainWindow` access during startup/shutdown
- **Medium: PO multi-line strings** — Parser now handles continuation lines and emits entries at EOF
- **Medium: CSV escaped quotes** — `""` inside quoted fields now correctly parsed as literal `"`

### Changed
- **Translation providers** — All providers use per-request `HttpRequestMessage` for auth headers (thread-safe with shared client)
- **Microsoft provider** — Implemented real Bing Translator API (batches up to 100 texts, region support)
- **OpenAI provider** — Implemented real OpenAI-compatible API (custom endpoint/model, batches 20 texts as JSON array)
- **Custom provider** — New webhook provider for custom HTTP translation endpoints
- **Provider settings wiring** — `PreTranslateViewModel` now loads provider config (API keys, endpoints) from `ProviderSettingsService`
- **Options dialog** — Sidebar navigation uses WPF-UI `ListView` with Fluent icons; Project settings separated to own page
- **Google SDK removed** — Removed unused `Google.Cloud.Translation.V2` NuGet package (~6 DLLs / 500KB)

### Added
- **GitHub Pages documentation** — `docs/` folder with Vue-rendered roadmap, Lucide icons, Mermaid diagrams
- **`CustomWebhookTranslationProvider`** — Supports any HTTP endpoint with configurable auth (Bearer or custom header)

## [0.6.0] — 2026-06-27

### Added
- **Plural forms support** — i18next suffix detection (`_one`/`_other`), ICU `{count, plural, ...}` parsing, generate missing forms
- **Gendered translations** — suffix detection (`_male`/`_female`/`_other`), ICU `{var, select, ...}` parsing, generate missing forms
- **Advanced placeholder validation** — extract/validate 7 placeholder formats across translations (named, indexed, printf, colon, template, ICU)

### Fixed
- **Title icon** — logo now properly shows in titlebar; window icon set for taskbar/alt-tab display

## [0.5.0] — 2026-06-27

### Added
- **Laravel PHP file support** — load/save `resources/lang/en/messages.php` nested arrays
- **Start screen responsiveness** — adaptive WrapPanel layout, max-width constraint
- **File association** — `.toucan.project` files open in Toucan (per-user registry)
- **Multi-selection in tree** — toggle selection, batch delete, select all/clear
- **Array support in JSON** — parse and reconstruct JSON arrays with `[N]` indexing

### Fixed
- **List view bug** — now shows full leaf keys instead of parent namespace nodes

## [0.4.0] — 2026-06-27

### Added
- **Custom language codes** — alias mapping in project settings (e.g. `zh-Hans` → `zh-CN`)
- **Auto-detect framework** — automatically selects format (ARB, RESX, PO, etc.) when opening a folder
- **Java .properties** — load/save ISO-8859-1 files with `\uXXXX` Unicode escapes
- **Skip unchanged on reload** — file watcher only triggers reload when timestamps actually change
- **RTL font support** — auto-detects RTL languages and applies appropriate font/flow direction

## [0.3.0] — 2026-06-27

### Added
- **Focused Editor: language subset selector** — Ctrl+E to toggle, prompt to pick languages
- **Zen mode: j/k keyboard navigation** — navigate between translation keys without mouse
- **Excel: Comment + Approved columns** — export/import approval status and comments
- **File watcher** — monitors project folder, prompts to reload on external changes
- **Suggestions panel** — fuzzy-match existing translations for the selected key
- **Focused Editor mode** — single-key form with prev/next for translator workflow
- **Zen mode** — F11 distraction-free editing (hides toolbar/statusbar/sidepanel)
- **PanelService** — centralized panel visibility management
- **Infinite scroll toggle** — switch between paginated and continuous modes
- **Excel (.xlsx) import/export** — ClosedXML-based, key + language columns
- **Statistics dialog** — per-language breakdown with progress bars
- **Tab navigation** between translation edit fields
- **Filter expression history** — last 15 searches persisted
- **Undo / Redo** — command-stack based (Ctrl+Z / Ctrl+Y)
- **KeybindingService** — centralized 24 shortcuts, visible in Options tab
- **TranslationPostProcessor** — parameter preservation + uppercase first letter
- **Plain text keys** option (no dot-splitting in tree)

### Changed
- Version bump to 0.3.0
- Roadmap reprioritized: AI/Source scanning moved to low priority
- Statistics uses proper dialog instead of MessageBox

## [0.2.0] — 2026-06-27

### Added
- **Undo / Redo** — command-stack based (Ctrl+Z / Ctrl+Y), records all translation value edits
- **KeybindingService** — centralized 20+ keyboard shortcuts in one file, removes XAML scatter
- **Keyboard Shortcuts tab** in Options dialog — displays all bindings in a GridView
- **Design system** — `Resources/DesignTokens.xaml` with shared spacing, margins, corner radius tokens
- **Import / Export** — full menu wiring using all 12 Core LoadStrategy/SaveStrategy formats
- **Framework tile grid** — New Project dialog redesigned with 16 selectable framework tiles
- **Comment field** per translation ID
- **Approved flag** — per-row toggle button with CheckmarkCircle icon
- **Spell checking** — enabled on all translation TextBoxes (WPF native)
- **Recent Projects flyout** — dynamic submenu showing last 10 projects with clear option
- **Start Screen** — restored with ShowStartScreen visibility binding, hides on load
- **Auto-open last project** on startup
- **Auto-select newly added ID** in tree after creation
- **Reveal in Explorer** — File menu command to open project folder
- **Duplicate ID** — deep-copies all translations with `_copy` suffix
- **Copy as template** — 3 configurable snippet patterns (Ctrl+1/2/3)
- **Filter bar** — Show translated / Show approved commands alongside existing Show untranslated
- **Translation context** — passed to DeepL/OpenAI via ProviderOptions
- **Formality setting** — wired to DeepL API (more/less/default)
- **Remember last translation service** — persisted in AppOptions
- **Preserve parameters** — regex protects `{{var}}`, `{0}`, `%s`, `:param`, `${var}` during translation
- **Keep uppercase first letter** — post-processor ensures translated text matches source casing
- **Plain text keys** — AppOptions flag to disable dot-splitting in tree view
- **Word-wrapping** for long namespace IDs in translation cards
- **Toucan icon** in TitleBar beside menu
- **Compact pagination** — 24×24 buttons, font 11, transparent styling
- **Convert case** commands (lowercase, UPPERCASE, Sentence, Title)
- **Trim whitespace** commands (trim, line-by-line, simplify)
- **Tree/List view** toggle in Views menu
- **Cut / Copy / Paste** for translation values via Edit menu
- **Set Filter** toolbar button wired
- **Show machine translations** toggle
- **TranslationPostProcessor** — centralized pre/post-processing pipeline for provider results
- **UndoRedoService** — singleton with 200-entry history cap

### Changed
- Search TextBox — changed from `UpdateSourceTrigger=PropertyChanged` to `LostFocus` + Enter key (stops live-filtering on every keystroke)
- Toolbar buttons — all use design token margins/paddings, consistent `ui:Button` usage
- Statistics icon → `ChartMultiple20`, Source icon → `Code20` (disambiguated)
- LanguagesView — `⋯` text replaced with `MoreHorizontal20` icon, CardExpander compacted
- GridSplitter — uses theme brush instead of hardcoded `#0CFFFFFF`
- LanguagesView Grid.Row fixed (was `3`, corrected to `2`)
- MainMenu — Import/Export icons swapped to correct semantic
- NewProjectViewModel — `Frameworks` changed from string list to `FrameworkTile` objects with Name/Description/Icon/SaveStyle

### Removed
- Source Control toolbar button (not implemented, was dead UI)
- All `IsEnabled="False"` dead menu items — either wired or removed
- Hardcoded `Window.InputBindings` block — replaced by `KeybindingService.Apply()`

### Documentation
- `docs/ROADMAP.md` — comprehensive feature parity tracker vs professional i18n editors (80% achieved)

---

## [0.1.1] — 2025-11-26

### Added
- StatusBar features: `StatusBarService`, `StatusBarViewModel`, `StatusBarView`
- `IProjectService` and `ISaveStrategy` interfaces
- ToolBarView and new project/translation commands
- Bulk action service (pre-translate, statistics)
- OptionsViewModel for centralized app options
- Multiple load/save strategies (Android XML, iOS Strings, XLIFF, ARB, CSV, RESX, TOML, PO)
- ProjectSettings model with manifest support
- Pre-translate dialog with preview
- Provider settings (DeepL, Google, Microsoft, OpenAI, Mock)
- Pagination system

### Changed
- Refactored UI/UX across all views
- Enhanced MainWindow layout with FluentWindow + TitleBar
- Improved tree/list converter, namespace handling

### Documentation
- Updated README for clarity

---

## [0.1.0] — 2025-11-19

- Initial release: basic JSON editing, tree view, language management
