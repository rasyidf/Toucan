# Changelog

All notable changes to this project will be documented in this file.

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
