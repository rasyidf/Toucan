# Changelog

All notable changes to this project will be documented in this file.

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

## [0.6.1] — 2026-06-27

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
- **Framework tile grid** — New Project dialog redesigned with 16 selectable framework tiles (BabelEdit-style)
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
- `docs/ROADMAP.md` — comprehensive feature parity tracker vs BabelEdit (80% achieved)

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
