# Changelog

## [Unreleased]

### Added
- **Continuous integration** — Pull requests and pushes to `main` and `release/**` build `Toucan.CrossPlatform.slnx` and run the Core and Avalonia tests on Windows, macOS and Linux, and upload coverage reports.

- **Format support matrix** — [docs/formats.md](docs/formats.md) lists, for every built-in format, the versions that work, what survives a save and what does not. It is generated from the code and checked by a test.
- **Unsafe saves are blocked** — Android XML projects that contain `<plurals>`, other resource types or `translatable="false"`, and RESX projects with non-string resources, `<metadata>` or files not named `Resources*.resx`, open with a warning, and saving them in place is refused, because Toucan would rewrite the files without that content. Save As still writes a copy.
- **Round-trip fixtures** — Every loadable format is checked for multiline text, CRLF, quotes, backslashes, unicode, placeholders, markup, separators, leading spaces, tabs, percent signs, `$`, and YAML-looking words and numbers.

### Fixed
- **Laravel PHP** — Multiline values were lost on load, and `\\` was read as two backslashes. The loader now reads single- and double-quoted strings, `array()` syntax, comments and nested arrays properly, and skips computed values.
- **TOML** — Backslashes, carriage returns, `\uXXXX` escapes and trailing comments are read correctly; literal `'strings'` are no longer unescaped.
- **Java properties** — A value with leading spaces keeps them, and `\r` and `\f` are escaped.
- **XML formats** — Carriage returns in Android XML, RESX and XLIFF values survive a save.
- **JSON numbers and booleans** — `10` and `true` are written back as a number and a boolean unless you edit them, instead of becoming `"10"` and `"True"`.
- **YAML flat keys** — A file that uses flat dotted keys (`"a.b.c": x`) is saved flat instead of being rewritten as nested maps, and quoted keys load without their quotes. A key that is also a parent (`app` and `app.title`) is written as flat keys instead of an invented `__self` entry.
- **YAML scalars** — `on`, `off`, `y`, `n`, `~`, numbers such as `1.0` and `007`, and hex-like text are quoted so YAML 1.1 readers keep them as text. Backslashes and carriage returns in quoted values load correctly.

## [0.20.2] - 2026-10-07

Format fixes (XLIFF no longer loses source text, notes and state on save; RESX and ARB language and metadata bugs), a new app icon, and documentation pages and a web manifest on the website.

### Added
- **Website documentation** — The plugin guide, AI Integration, provider settings, architecture, roadmap, known issues and the other guides are now pages on the website, behind a new Docs index, instead of links to GitHub. `python3 tools/build-docs.py` builds them from `docs/*.md`.
- **Web manifest** — The website has a `site.webmanifest` and the full icon set, including maskable icons.

### Changed
- **App and website icon** — A new toucan icon on a rounded black square replaces the old logo in the app (window, title bar, macOS and MSIX packages) and on the website.
- **Website styles** — `site.css` is split into `base.css` (shared by every page), `home.css`, `changelog.css` and `doc.css`, and the theme toggle moves to a shared `site.js`.

### Fixed
- **XLIFF save no longer loses data** — Saving wrote the key as `<source>` and the first language as `source-language`, and dropped notes, state, `datatype`, `original` and other elements. The source text is now loaded and written back with the unit's notes, attributes and extra elements (such as Angular's `context-group`). Units go back to the file they came from instead of a new `<language>.xlf`, untranslated units keep their source, a unit gets `state="translated"` once it has a translation, and XLIFF 2.0 files stay 2.0. An untranslated unit now shows an empty value instead of its source text.
- **RESX language detection** — Only a real culture name counts as a language. `Views.Home.Index.resx` and `Resources.Designer.resx` are no longer read as languages `Index` and `Designer`.
- **ARB metadata** — `@key` objects (description, placeholders) and other `@@` header entries such as `@@last_modified` survive a save, so Flutter's `gen-l10n` keeps building.
- **ARB region locales** — `app_en_US.arb` loads as `en_US` (and `intl_zh_Hans_CN.arb` as `zh_Hans_CN`) instead of `US`, and saves back to the same file. `@@locale` still wins over the file name.

## [0.20.1] - 2026-10-07

Documentation and website update: a browsable changelog, a smaller release plan through v0.30, and refreshed architecture and branding guidance.

### Added
- **Website changelog** — A searchable release history with expandable notes, version permalinks, and the website’s shared light/dark theme, linked from the homepage and roadmap.

### Changed
- **Roadmap** — Split the path to v1.0 into ten focused releases, v0.21–v0.30, with completion gates and separate milestones for snapshot sources, CRUD synchronization, and the first online connector.
- **Documentation** — Refresh application and Core architecture, branding guidance, and the docs index; align the README and website roadmap.
- **Website theme** — Share the homepage and changelog styles and adjust responsive navigation for the new page.

## [0.20.0] - 2026-10-07

AI Integration: one app-wide switch, off by default, for Claude, OpenAI-compatible servers and Gemini, with prompts you can read and edit and a new Clarity check for source strings. API keys move to one encrypted secret store. Also: color schemes, an update check, search options, per-project encoding and line endings, pinned recent projects, ghost-text suggestions and a keyboard shortcut sheet.

### Added
- **AI Integration** — AI is now its own integration under Settings → AI, separate from machine translation. One switch turns every AI feature on or off for the whole app. It is **off** by default, and a first-run onboarding step asks whether to use it. Pick the service (Claude, OpenAI or a compatible server such as Ollama, or Gemini), the model, endpoint and key, and test the connection before saving. See [docs/ai-integration.md](docs/ai-integration.md).
- **Open, editable prompts** — Every AI feature's system prompt is a plain file. The defaults ship in [`Toucan.Core/Ai/Prompts`](Toucan.Core/Ai/Prompts). Edit them under Settings → AI → Edit prompt… for all projects (`Documents/Toucan/prompts`) or for one project (`.toucan/prompts`, safe to commit). Prompts take `{{variables}}` and `{{#sections}}`, and a variables panel lists what each prompt can use.
- **Clarity AI** — Translate → AI → Check Source Clarity… reviews source strings for ambiguous words, strings too short to translate without context, concatenation, idioms and unclear placeholders. It suggests a clearer source string and a note for translators. Findings go to the Issues panel.
- **Secret store** — Every API key and token, for providers and AI services, is kept in one encrypted store in your user profile (`secrets.json` next to `secret.key`), never in `providers.json` or a project folder. Settings → Data & privacy lists stored secret names and removes them.
- **Pin recent projects** — Pin a project from the Start screen or from Settings → General → Recent projects. Pinned projects stay at the top of the list (Start screen and Open Recent), and they do not count toward the list limit. The old WPF pin button never saved anything; this one does.
- **Recent projects settings** — Choose how many projects to remember (1–50), whether Clear keeps pinned projects, and manage the list (pin, unpin, remove) from Settings → General.
- **Preferred language from recent projects** — With "Detect preferred language from recent projects" on, the source language for new projects, and for projects that name none, comes from the most recently opened project instead of the Default language setting. Off by default.
- **Ghost-text suggestions** — When you focus an empty translation field, the best translation-memory match for its source text appears inside the field in faint italics. Press Tab to accept it; type to ignore it. Follows Settings → Translation memory → auto-suggest and the similarity threshold.
- **Keyboard shortcut sheet** — Cmd/Ctrl+/ (or Help → Keyboard Shortcuts) shows every shortcut grouped by menu. Esc or a click outside closes it.
- **Color schemes** — Settings → Appearance offers accent presets (Toucan, Ocean, Forest, Sunset, Rose, Violet, Graphite) or a custom accent and per-color edits, separately for light and dark. Colors preview live; Save keeps them, Cancel undoes them.
- **Update check** — Settings → About → Check for updates looks at GitHub Releases and says whether a newer version exists. The release channel (Stable or Preview) decides whether pre-releases count. It never downloads or installs anything.
- **Search options** — The Search panel has match case, whole word and regex toggles, highlights matches in the results, and Replace uses the same options.
- **Encoding and line endings per project** — Click the encoding or line-ending item in the status bar to choose UTF-8 or UTF-8 with BOM, and LF or CRLF, for the project's translation files. Applied on the next save. Java `.properties` keeps ISO-8859-1.

### Changed
- **Claude, OpenAI and Gemini are AI services, not translation providers** — Machine translation lists one **AI** provider instead. It uses the AI service and the editable Translate prompt from Settings → AI, and is only offered while AI is on. On first run, the old providers' endpoint, model, key and custom prompt move to AI Integration, and a project or preference that names one of them gets the AI provider. The CLI still accepts `-p claude`, `-p openai` and `-p gemini` as `-p ai`, and `TOUCAN_AI_BACKEND` turns AI on for one CI run.
- **Analyze with AI** — Uses AI Integration and its editable Analyze prompt instead of the OpenAI provider's settings, so it works with Claude and Gemini too. It replaces only earlier AI findings in the Issues panel, not validation results.
- **Provider keys** — `providers.json` (app-wide and per project) no longer stores keys; values move to the secret store the next time provider settings are saved. Keys written by older versions are still read.
- **CLI** — `toucan translate` prints why items failed, and passes the project's context and prompts to the provider.
- **Built-in modules** — The built-in formats, providers, validation rules and framework profiles now load as compiled-in modules, the same way plugins do. Settings → Plugins lists them (read-only, with versions) after your plugins, and `toucan plugins list` shows them too. Settings → Validation lists every registered rule, plugin rules included. Plugin API stays 1.0.
- **Settings lists** — Lists in Settings, Project Properties and Provider Settings (recent projects, colors, copy templates, AI features, rules, secrets, built-in modules) share one style, with search on longer lists and a message when a list is empty.

### Removed
- **WPF app source** — The WPF app is no longer in `main`; its source is on the [`legacy/wpf`](https://github.com/rasyidf/Toucan/tree/legacy/wpf) branch. Its last release was 0.17.3.

## [0.19.0] - 2026-10-06

Command palette, a new title bar, Claude and Gemini translation providers, and a UI polish pass across the Avalonia app. The Avalonia app is now the Toucan app on Windows too; the WPF app is deprecated and 0.17.3 was its last release.

### Added
- **Claude and Gemini translation providers** — Pre-translate with Anthropic Claude (Messages API) or Google Gemini (a free AI Studio key works). Both appear in the Machine Translation panel, the Pre-translate dialog and Provider Settings, send up to 20 texts per request, add the app context and formality to the prompt, and report the provider's error message. See [docs/provider-settings.md](docs/provider-settings.md).

- **Command palette (Avalonia app)** — Cmd/Ctrl+Shift+P (or the pill in the title bar) opens a searchable list of every menu command with its shortcut. Type words in any order, Up/Down and Enter to run, Esc to close; commands that cannot run right now sink to the bottom. It reads the menu definitions, so new menu items appear in it automatically.
- **Title bar** — Logo and name, a save-state chip (`Unsaved` / `Saved`, click to save) and the palette pill centered in the bar. On macOS the Zen-mode header now clears the window controls.
- **Settings search** — A search box above the Settings sidebar filters individual settings across all pages.
- **Screenshot harness** — `TOUCAN_TEST_SCREENSHOTS` renders every window, panel and dialog headlessly; see [docs/visual-review.md](docs/visual-review.md).

### Changed
- **Windows** — The Avalonia app (tested on Windows) is the supported app on Windows, macOS and Linux. The WPF app is deprecated; its last release is 0.17.3. Windows builds ship as a portable x64 zip for now.
- **UI polish (Avalonia app)** — Settings and Project Properties use a left page list with grouped rows; Settings has a search box that filters individual settings; plugin cards show status, details and actions; New Project, Provider Settings, Statistics, Import and Pre-translate use the same grouped style; the side panels, pager and hidden-namespaces footer are tidied.

### Fixed

- **PO round trip (FMT-01 save, FMT-02, FMT-03)** — plural entries (`msgid_plural`, `msgstr[N]`) are kept, `msgctxt` is kept apart from `msgid` (key is `msgctxt` + U+0004 + `msgid`, as gettext does), and `#,` flags, `#:` references, translator comments and the header survive a save. `#, fuzzy` maps to "not approved". Entries are written back to the file they came from (for example `fr/LC_MESSAGES/messages.po`) instead of a new `<language>.po` at the root.

- **PO language detection (FMT-01)** — `<lang>/LC_MESSAGES/<domain>.po` projects now load one language per folder; the language comes from the `Language:` header, then the folder, then the file name.
- **CSV multi-line values (FMT-04)** — the loader parses RFC 4180 records, so quoted values with line breaks are no longer cut off; the saver now quotes values containing a lone `\r`.
- **Right-click menus** — The editor cards' menu is now a flyout like the Explorer's, with icons and Indonesian labels (the old menu type never appeared on macOS).
- **Pre-translate** — The source language is no longer offered as a target language; the preview text is selectable; empty language lists show a message.
- **DeepL requests** — Free-plan keys (ending in `:fx`) now use `api-free.deepl.com` automatically; the key is sent in the `Authorization` header instead of the request body; `en-US` sources and `fr-FR` targets are sent as DeepL expects (`EN`, `FR`); More/Less formality is now applied (as `prefer_more`/`prefer_less`); errors include DeepL's message.
- **Explorer right-click menu on macOS** — The key menu (Add Key, Rename, Delete, …) did not appear; it is now a flyout like the app's other menus.

## [0.18.1] - 2026-10-06

Indonesian localization, a Machine Translation panel, `.tproj` file association and a proper macOS installer. Windows stays on 0.17.3.

### Added
- **Localization (Avalonia app)** — Menus, panels, dialogs, settings, tooltips, pagination and the status bar are translated to Indonesian (id-ID); choose it under Settings → General → Interface language and restart. Translations are embedded JSON maps keyed by the English text (`Locales/Strings.{culture}.json`), so a missing entry falls back to English, and a test fails when a UI string has no translation. Messages built at run time are still English.
- **Edit menu** — Cut Key Values and Trim Line by Line; copy-template shortcuts now cover all five templates.
- **WPF parity record** — [docs/archive/wpf-parity.md](docs/archive/wpf-parity.md) lists what the Windows app had, what moved, and what was deliberately left behind, so the WPF app can be retired.
- **Machine Translation panel (Avalonia app)** — Right-hand panel with a provider picker built from the registered providers (plugin providers included, with a "Needs API key" badge), quick actions (translate selected key, fill empty values in view, Pre-translate, provider settings), and the last run's results. The Pre-translate dialog now lists the same registered providers, and the provider chosen there carries over to the panel.
- **`.tproj` file association (Avalonia app)** — Settings → Integration registers Toucan for `.tproj` files: per-user registry keys on Windows, a `.desktop` file and MIME type on Linux. On Windows it also manages the "Open with Toucan" folder menu entry. `Toucan.app` now declares the type in its Info.plist, and the app opens files that Finder passes to it.
- **macOS DMG installer** — Releases ship `Toucan-<version>-osx-<arch>.dmg` with the usual drag-to-Applications window, next to the zip. `packaging/build-macos-app.sh` writes it (styled when `create-dmg` is installed, plain `hdiutil` otherwise).
- **macOS signing and notarization (optional)** — `packaging/build-macos-app.sh` signs with a Developer ID and the hardened runtime when `SIGN_IDENTITY` is set, and notarizes when `NOTARY_PROFILE` is also set. Without them the build is ad-hoc signed as before.

### Fixed
- **macOS app icon** — `Toucan.app` had no icon and showed as a blank gray item in Finder and the Dock; the build now generates `AppIcon.icns` from the logo.

### Docs
- First-launch steps for macOS Gatekeeper now match macOS 15: Privacy & Security > Open Anyway, since right-click > Open no longer bypasses it.

## [0.18.0] - 2026-10-01

First macOS and Linux release. The Avalonia app now targets `net10.0` (it targeted `net10.0-windows` before, so no earlier version ran on macOS or Linux). The Windows build of 0.18.0 is not out yet; Windows stays on 0.17.3 until it is.

### Added
- **Plugin system (preview, Avalonia app and CLI)** — Plugins are .NET assemblies in `Documents/Toucan/plugins/<id>/` with a `plugin.json` manifest. They can add file formats, translation providers, validation rules and framework profiles. See [docs/plugins.md](docs/plugins.md) and `samples/Toucan.Sample.Plugin`.
- **`Toucan.Plugins.Abstractions`** — Small contract assembly and NuGet package (plugin API 1.0) that plugin authors reference; Toucan supplies it at run time so types match.
- **Trust model** — Plugins load only when enabled and when you have trusted their exact files (SHA-256 of the plugin folder); changed plugins ask again. Settings → Plugins page, a startup prompt for untrusted plugins, and a signature seam (plugins show "Not signed"; signing becomes mandatory later).
- **CLI** — `toucan plugins list|trust|revoke|enable|disable`, `--allow-plugin <id>` for a single run, `TOUCAN_PLUGINS_DIR` and `TOUCAN_PLUGIN_POLICY`.
- **Plugin guide and sample** — [docs/plugins.md](docs/plugins.md) and `samples/Toucan.Sample.Plugin` (tab-separated-values format plus a TODO/FIXME rule). The sample is built and exercised by the test suite, and verified to build against the packed NuGet package alone.
- **Plugins page and menu entry** — Settings → Plugins lists every installed plugin with status, what it provides, signature, hash and folder, with enable, Trust… and Revoke actions; Settings menu → Plugins… opens it.
- **Manifest `entryType`** — Optional, for assemblies that contain more than one plugin class.
- **Cross-platform solution** — `Toucan.CrossPlatform.slnx` builds and tests everything except the Windows-only WPF projects.
- **macOS and Linux builds** — `Toucan.app` for Apple silicon and Intel, and self-contained tarballs for Linux x64 and arm64.
- **Avalonia app (macOS and Linux)** — Zen mode, Editor/Review/Audit modes, and side panels (Explorer, Search, Issues, Source Code, Translation Memory, Languages, Inspector).
- **macOS app bundle** — `packaging/build-macos-app.sh` builds a self-contained, ad-hoc-signed `Toucan.app`.
- **Tests** — 275 Core tests (format round trips and conventions, composition root, plugin host, trust policy, sample plugin) and 48 Avalonia tests (plugin prompt and page, headless render).

### Changed
- **Formats are identified by string IDs** (`json`, `android-xml`, `po`, …) instead of the `SaveStyles` enum. Project files now store `"saveFormat"`; old `"saveStyle"` values are migrated on load and not written back. A project whose format is not installed now fails to open with a "format unavailable" message instead of being read as JSON.
- **Format conventions live on the format** — default file path, language files, comment storage and detection rules come from each save strategy, replacing four duplicated tables. The export and import pickers list whatever formats are registered.
- **Shared composition root** — `AddToucanCore()` is used by the app and the CLI. The CLI now supports every format, including Java `.properties` and Laravel PHP, and takes providers from the same container.
- **Translation provider settings** are built from the registered providers, so plugin providers appear alongside the built-ins.
- `ValidationContext.Settings` became `ValidationContext.PrimaryLanguage` (rules only ever read the primary language).
- **CLI** — `export -f` accepts a format ID (`android-xml`) or the old enum name (`AndroidXml`); `list-formats` prints IDs with display names. The export picker in the app lists formats in registration order rather than a hand-written order.
- **One version number** — The app version is set once, in `Directory.Build.props`. `publish.ps1`, the Inno Setup script, `packaging/Build-Msix.ps1` and `packaging/build-macos-app.sh` read it. The v0.17.3 builds reported assembly version 0.17.2, and the CLI reported 1.0.0.
- **Docs and website** — The website (`docs/index.html`), README, roadmap, and shipped-feature list now agree on versions (0.18.0 for macOS and Linux, 0.17.3 for Windows), platform status, and the road to 1.0 from `docs/todos/future-roadmap.md`.
- **Core test project** targets plain `net10.0` and references the test SDK, so `dotnet test` works on macOS and Linux.

### Breaking changes (for code built on `Toucan.Core`; the WPF projects still need updating)
- `ISaveStrategy` / `ILoadStrategy` expose `string FormatId` instead of `SaveStyles Style`; `ISaveStrategy` also requires `DefaultFilePath(language)`.
- `ITranslationStrategyFactory` looks formats up by ID and exposes `SaveStrategies`; `IProjectService.CreateProject/CreateLanguage/Save` and `ICommentPersistenceService` take a format ID (`SaveStyles` overloads remain for the comment service and the factory). `IProjectService` gains `GetDefaultFilePath` and `GetLanguageFiles`; `IProjectLifecycleService.CreateAndOpenProjectAsync` takes a format ID.
- `IFrameworkProfile.DefaultFormat` is now `DefaultFormatId` (string).
- `ProjectSettings.SaveFormat` replaces `SaveStyle` as the stored value (`SaveStyle` remains as a compatibility property that returns JSON for plugin formats).
- `ProjectOpenStatus.FormatUnavailable` and `FormatUnavailableException` are new; projects with an unavailable format no longer fall back to JSON.
- The plugin-facing types moved to `Toucan.Plugins.Abstractions` with their namespaces unchanged, so source compiles but the assembly reference is new.

### Fixed
- **API keys on macOS and Linux** — Provider API keys are encrypted with AES-GCM and a per-user key file (owner-only permissions), because DPAPI only exists on Windows. Before this change the Avalonia app fell back to base64 when DPAPI was unavailable.
- New projects in Gettext PO, INI, Java `.properties` and Laravel PHP formats no longer default to `en.json`.
- Save As on a project no longer resets its format to JSON when the format came from a plugin.

## [0.17.3] - 2026-07-06

### Added
- **Two-tier settings architecture** — New `ProjectDefaults` template (`project-defaults.json`) provides default values for new projects and acts as fallback when a project doesn't override a field.
- **"Manage Project Defaults" window** — 5-page master-detail editor (Editor, Translation, Validation, Source Code, Features) accessible from Settings → Editor.
- **Validation settings page** — Per-rule enable/disable toggles with severity selection (Error/Warning/Info) for all 6 built-in rules.
- **Translation Memory settings page** — Similarity threshold slider, scope selector (Global/Project), auto-suggest toggle, max suggestions, clear/import/export buttons.
- **Source Code settings page** — Framework presets dropdown (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, Generic JSON) that auto-populates scan extensions, excluded dirs, locale folder pattern, and key matcher regex.
- **Data & Privacy settings page** — Clear filter history, recent projects, TM data; export/import settings backup; reset to factory defaults.
- **Update panel in About** — "Check for updates" and "Update channel" (Stable/Preview) UI shell for future auto-updater.
- **Copy templates CRUD** — Dynamic 1–5 template list with add/remove (replaces fixed 3 textboxes) across app settings, project defaults, and project properties.
- **Reusable `LanguageListEditor` component** — Shared WinUI-style card list for language management, used by both Manage Languages dialog and Suggested Languages settings.
- **`EffectiveSettingsResolver`** — Static helper that merges nullable project fields with defaults for consumption by the engine.
- **Framework-connected source code config** — Locale folder pattern and key matcher regex fields, auto-populated from framework presets.

### Changed
- **Settings dialog expanded to 12 pages** — General, Appearance, Editor, Translation, Validation, Translation Memory, Source Code, Keyboard Shortcuts, Languages, Integration, Data & Privacy, About.
- **Settings sidebar** — VS Code-style left accent border indicator on selected item with proper card background; no rounded corners, flush left edge.
- **Activity bar** — 3px left accent border on active panel with card background; properties moved into Style triggers for proper WPF precedence.
- **"Add ID" → "Add Translation Key"** — Renamed in menu, toolbar, keybinding list, and dialog title.
- **PromptDialog redesigned** — Mica backdrop, `ui:TextBox` with icon and placeholder, validation (won't dismiss on empty), "Add" button instead of "OK".
- **Mode selector bar** — Increased height (26→30px), font size (11→12px), adjusted padding to prevent text cropping.
- **About menu** — Now opens unified Settings dialog at the About tab instead of a standalone window.
- **Plain text keys** — Changed from CheckBox to ToggleSwitch control.
- **Validation severity ComboBox** — Widened from 100→120px to prevent "Warning" truncation.
- **TM Scope ComboBox** — Widened from 140→200px.
- **Copy templates** — Upgraded from raw Border+TextBox to proper `ui:CardControl` list items.
- **Project Languages list** — Upgraded from plain text list to `ui:CardControl` card items.
- **Suggested Languages** — Now uses shared `LanguageListEditor` component with proper WinUI card styling.
- **Settings dialog alignment** — Sidebar and content top padding aligned (4px) across all 3 settings dialogs.
- **Startup toggle** — Moved from Integration page to General page.
- **App language list** — Expanded from 2 to 8 languages.

### Added (AppOptions model)
- `BackdropType`, `FontSize`, `TmAutoSuggest`, `TmMaxSuggestions`, `DefaultProjectLanguages`, `CopyTemplates` (list replacing Template1/2/3).

### Added (ProjectSettings model)
- `ValidateOnSave`, `ValidationRules`, `ScanExtensions`, `ExcludedDirectories`, `AutoScanOnOpen`, `PreservePlaceholders`, `PreviewBeforeApply`. Made editor/feature fields nullable for override semantics.

### Removed
- **Standalone `AboutDialog`** — Replaced by About page in Settings. `AboutViewModel` and DI registration removed.

## [0.17.2] - 2026-07-06

### Fixed
- **Issues panel grouping** — Validation issues now grouped by rule type with headers showing counts. Right-click context menu to dismiss one, dismiss all of a type, or dismiss all.
- **Search panel sizing** — Regex/chevron buttons and Search button now align properly with TextBox and ComboBox heights.
- **Source Code panel** — Removed excess top padding on filter. "Open Settings" now opens project settings (not app preferences). Post-scan empty state shows "No key usages found" instead of the configure buttons.
- **Explorer list view foreground** — Removed hardcoded gray text; items now use theme-aware text brush matching the tree view.
- **Status bar click actions** — Mode badge cycles Editor→Review→Audit. Translation stats runs validation and opens Issues panel. Git status focuses the Source Control panel.

## [0.17.1] - 2026-07-06

### Fixed
- **DiffMergeEngine dirty tracking** — Merged items (added/modified from disk) now update their baselines via `MarkSaved`, preventing perpetually-dirty state after non-conflicting merges.
- **AutoSaveService dispose crash** — Guarded semaphore release against `ObjectDisposedException` when `Dispose()` is called during an in-flight save; marked `_disposed` volatile for cross-thread visibility.
- **TranslationManagementService double-fire** — Eliminated TOCTOU race in `RaiseDirtyStateChangedIfNeeded` by computing dirty state inside the lock, preventing duplicate `DirtyStateChanged` events.
- **External reload threading** — Auto-reload and merge paths now dispatch through a UI-thread marshaler (`SetUiDispatcher`), preventing cross-thread updates to UI-bound collections.
- **iOS .strings escape corruption** — Replaced chained `string.Replace` with single-pass character scanner; `\\n` in .strings files now correctly produces literal backslash+n instead of a newline.
- **Java .properties line continuations** — Added `JoinContinuationLines` that handles trailing-backslash multi-line values per the spec; multi-line values are no longer truncated to the first line.

## [0.17.0] - 2026-07-03

### Added
- **Search & Replace (FG-07)** — global search across all keys/values/languages with regex support, scope filtering, replace preview, and search history.
- **Bulk Operations (FG-08)** — multi-select mode with bulk delete, move to namespace, pre-translate, approve, and copy source→target language.
- **Custom Validation Rules (FG-09)** — configurable per-project rules (max length, forbidden words, regex patterns) with severity levels and auto-fix suggestions.
- **TM Enhancements (FG-03)** — TMX import/export, TM entry management (view/delete/clear), ghost text suggestion property, configurable similarity threshold and scope.

## [0.16.1] - 2026-07-03

### Improved
- **Toolbar removed** — ModeSelectorBar and panel toggles moved into TitleBar.TrailingContent for a cleaner layout.
- **Inspector split** — Languages panel separated from Inspector; each is now its own right-side panel in the activity bar.
- **Inspector redesign** — Details and Suggestions tabs use Fluent bordered cards with contextual icons instead of plain text.
- **PanelHost actions slot** — panel-specific action buttons (add, refresh, settings) now render beside the ellipsis menu in the panel header.
- **ActivityBar context menu** — right-click to show/hide individual panels with checkmarks.
- **Duplicate headers removed** — ResourcesView, LanguagesView, IssuesPanel, and TranslationMemoryPanel no longer have redundant internal headers; PanelHost provides the unified header.

### Fixed
- **ManifestLoadStrategy fallback** — projects with empty `translationPackages` now fall through to format-based file scanning instead of returning nothing.
- **ProjectSettings.Save() backfill** — saving the manifest auto-populates `translationPackages` from `Languages` when empty, preventing the "no translations" bug after adding a language via Manage Languages.

## [0.15.0] - 2026-07-02

### UI Revamp
- Three-pane VS Code-style layout (sidebar, editor, inspector)
- Editor modes: Editor, Review, Audit with mode selector and auto-filter
- Zen mode overlay with J/K navigation
- Snipping Tool-style minimal toolbar with pill-grouped buttons
- Photos-style footer action bar (borderless buttons, contextual actions)
- Segmented menu bar (File, Edit, Tools, Find, View, Help)
- Toolbar hidden on start screen, Mica backdrop visible
- Default sidebar widths 200px (1:3:1 ratio)

### Component Extraction
- Split OptionsDialog into 8 page UserControls (Views/Settings/)
- Split NewProjectPrompt into FrameworkStep + LanguagesStep
- Extracted LanguageGroupCard + TranslationRow from LanguagesView (400→92 lines)
- New reusable components: DialogFooter, SettingsCard, PanelHeader, LanguageChip
- Design tokens standardized (DesignTokens.xaml)
- Accessibility pass on all dialogs (AutomationProperties, FocusManager, Mica)

### System Integration
- Project manifest renamed: `toucan.project` → `toucan.tproj` (.tproj extension)
- FileAssociationService: Install/Clear/IsInstalled API, OpenWithProgids
- File open dialog supports .tproj filter
- Startup args now handle file paths (resolves to parent directory)

### CLI
- Added `toucan translate` command (batch pre-translate with provider selection, dry-run, language targeting)
- Updated help with translate options and examples

### Bug Fixes (U1–U5)
- U1: OptionsViewModel BrowseSourceRoot/BrowseSourceEditor/ConfigureLanguageCodes now functional
- U2: IUnsavedChangesHandler wired — prompts save/discard/cancel on close
- U3: FilterUsedKeys/FilterUnusedKeys now actually filters based on source scan
- U4: ShowMachineTranslations toggle filters to unapproved+filled items
- U5: Shortcuts page shows info banner about read-only status

### Branding
- Logo color: purple → blue (#2196F3)
- Copyright: 2023–2026
- AssemblyInfo/csproj: description updated to "Professional translation resource editor"
- Homepage/docs URLs → https://toucan.rasyid.dev
- Splash screen: fixed 78-byte placeholder, replaced with real image
- Version bumped to 0.15.0
- LICENSE.txt: filled in placeholder with real name/years
- Branding doc (docs/branding.md) rewritten with current design decisions

### Documentation
- Full docs reorganization: deleted 12 stale files, merged content, renamed to lowercase-dash
- README rewritten with accurate feature list and roadmap
- UI revamp plan finalized (all phases complete except Phase 5 performance)
- UI polish plan completed
- Panel extension system plan documented (docs/archive/panel-extension-plan.md)

### Additional Fixes
- TitleBar icon: fixed crash by using `ui:ImageIcon` (correct `IconElement` type for WPF UI 4.x)
- Window Title property set — taskbar now shows "Toucan"
- Menu toggle states: checkable items show current state (tree/list, zen, panels, etc.)
- Menu disabled states: Edit/Tools/Find/Save/Close/Reveal/Properties disabled when no project loaded
- Start screen: recent projects now clickable with hover state and delete button
- Start screen: added Quick Tips section with keyboard shortcuts hints
- Start screen: "Open last project on startup" checkbox (persists to settings.json)
- Replaced all emoji with Fluent icons (🕒→History20, ⚡→Flash20, ✕→Dismiss16)
- ResourcesView: removed ui:Card wrapper for proper edge-to-edge content
- StatusBar: fixed vertical centering (32px height, VerticalAlignment=Center on grid)
- Footer buttons: custom PanelButton template with subtle CornerRadius=4 hover/pressed states

## [0.14.3] - 2026-07-01

### Added
- **Modular StatusBar architecture** — Status bar is now composed of independent panels (`IStatusBarPanel`) managed by a `StatusBarPanelRegistry`. Each panel can be shown/hidden, reordered, and clicked.
- **10 built-in panels**: VCS (branch + changes + sync), Translation Stats (progress + per-language breakdown), Mode (EDITOR/REVIEW/AUDIT badge), Project (name + dirty count), Status (ephemeral text), Language (primary + switcher), Encoding (UTF-8), Line Endings (LF/CRLF toggle), Notifications (badge), Loading (spinner).
- **Dynamic panel rendering** — StatusBarView uses `ItemsControl` with implicit `DataTemplate` per panel type. Left/Center/Right alignment groups rendered independently.
- **Panel click actions** — Each panel has a `ClickCommand`: VCS opens summary, Stats opens statistics dialog, Language opens switcher, Line Endings toggles LF↔CRLF, etc.
- **Panel registry API** — `StatusBarPanelRegistry.Register()`, `.Unregister()`, `.SetVisibility()`, `.Reorder()` — external services can add custom panels at runtime.
- **Rich tooltips** — VCS shows branch + change summary, Stats shows per-language progress bars, all panels have contextual tooltips.

### Changed
- **StatusBarViewModel** — Collapsed from 4 partial files into a single file backed by panel instances. Backward-compatible: existing callers (`StatusBarService.UpdateStatus()`, `.UpdateDefaultLanguage()`, etc.) still work unchanged.
- **StatusBarView.xaml** — Replaced hardcoded 8-column Grid with 3-column layout (Left/Center/Right) using `ItemsControl` bound to registry collections.

## [0.14.2] - 2026-07-01

### Improved
- **Centralized FileEnumerator** — Extracted `Toucan.Core\Services\FileEnumerator.cs` with flags-based `EnumerateOptions`. All load strategies (JSON, CSV, YAML, PHP) now use a single file crawler with shared directory exclusion list. `SkipNestedLocaleDirs` flag auto-detects when root has language-code subdirs and skips nested `locales/`, `i18n/`, `translations/`, `lang/` directories to prevent duplicates.
- **UpdateSummaryInfo per-keystroke** — `TranslationDetailsView` now debounces the update event (300ms idle) instead of firing on every KeyUp.
- **JSON key order instability** — `JsonSaveStrategy` now sorts items by namespace before writing, producing stable key order across saves (reduces VCS noise).
- **Placeholder count validation** — `PlaceholderService.Validate` now uses count-aware comparison instead of set-based `Except`. Detects when a placeholder appears more times in source than target.

## [0.14.1] - 2026-07-01

### Fixed
- **Tree corruption on rename** — `RenameItem` used `string.Replace` which corrupted unrelated keys sharing a substring (e.g., renaming "app" mangled "application"). Now uses exact prefix + dot delimiter matching.
- **Tree corruption on delete** — `DeleteItem` used bare `StartsWith` which deleted sibling keys (e.g., deleting "app" also deleted "appSettings"). Now requires exact match or dot-separated child.
- **NsTreeItem lazy-load flattening** — The `Items` getter was flattening grandchildren into the current node via `AddRange(child.Items)`. Children now retain their subtree structure.
- **YAML round-trip data loss** — Save strategy now properly escapes `\n`/`\r`/`\t` in double-quoted values, quotes YAML reserved words (`true`, `false`, `yes`, `no`, `null`), and preserves keys that are both parents and leaf values. Load strategy now supports multi-line block scalars (`|` and `>`).
- **Undo history corruption** — After 200 edits, the undo stack cap logic reversed item order (newest ended up at bottom). Now iterates in reverse when repopulating.
- **Dirty tracking bypass** — `TranslationItemViewModel.SaveTranslation()` now calls `ITranslationManagementService.NotifyValueChanged()` so the project correctly shows unsaved state.
- **Silent data loss on close** — `Window_Closing` now prompts Save/Discard/Cancel when unsaved changes exist, instead of silently discarding edits.
- **Source code scan thread-safety** — Replaced `List<KeyUsage>` with `ConcurrentBag<KeyUsage>` in parallel scan. Fixed case-sensitive directory exclusion on Windows (now uses `StringComparer.OrdinalIgnoreCase`).
- **Provider mock fallback** — All providers (OpenAI, Google, DeepL, Microsoft) now report `Succeeded = false` with "No API key configured" instead of silently injecting fake `[provider/lang]` translations.
- **Provider language code truncation** — Removed `Split('-')[0]` that stripped regional variants. Full BCP-47 codes (e.g., `zh-CN`, `pt-BR`, `EN-US`) are now passed to translation APIs.
- **Google Translate HTML entities** — Response text is now decoded via `WebUtility.HtmlDecode()` to fix garbled apostrophes and ampersands.
- **Statusbar language selector empty** — `AvailableLanguages` collection is now populated from project languages after load, so the inline language switcher works.
- **Duplicate FileWatcherService** — MainWindow now uses the DI-registered singleton `IFileWatcherService` instead of creating its own instance. External-change detection is consistent with the lifecycle service.
- **CreateNewItem false duplicate** — Replaced `Namespace.Contains(newNamespace)` with exact `==` match. Creating "app" is no longer blocked by "wrapper.app.title".
- **Delete/F2/Escape in TextBox** — `HandleZenKeys` now guards these keys when a TextBox has focus, preventing accidental item deletion or rename while typing.
- **FileWatcherService race condition** — Replaced `bool _pending` with `Interlocked.CompareExchange` to prevent missed/double-fired change events from concurrent threads.
- **PreTranslateViewModel CTS leak** — Previous `CancellationTokenSource` is now disposed before creating a new one on each Start().
- **Provider settings lost on switch** — `RebuildFieldItems` now flushes field edits to the *previous* selection before clearing, so switching providers no longer discards unsaved config.
- **NewProjectViewModel deadlock** — Converted `NextStep()` from sync (`GetAwaiter().GetResult()`) to `async Task`, eliminating potential UI thread deadlock on the existing-project dialog.

## [0.14.0] - 2026-06-30

### Added
- **Provider Settings integration** — Providers (Google, DeepL, Microsoft, OpenAI, Custom Webhook) now have proper schema definitions with default values. API keys are stored encrypted via DPAPI. Pre-translation works out of the box once an API key is saved.
- **Project Properties dialog** — Standalone dialog (File → Project Properties, or toolbar button) with 5 pages: Identity, Translation, Editor, Features, Source Code. Separated from the global Options dialog.
- **Namespace hiding** — Right-click any namespace in the tree to hide it from the editor and statistics. Manage hidden namespaces in Project Properties → Editor. Persisted in `toucan.project`.
- **Session dirty tracking** — Modified keys show a caution-colored left indicator bar. Statusbar displays a dirty count badge. Cleared on save.
- **BreadcrumbBar** — Added WPF-UI BreadcrumbBar to the Resources panel showing the selected namespace path.
- **Fluent MessageBox** — All user-facing message dialogs now use `Wpf.Ui.Controls.MessageBox` instead of native Win32 MessageBox.
- **Default project folder** — New Project dialog defaults to `Documents/Toucan/{project-name}`, auto-updating as you type.
- **Existing project detection** — New Project wizard detects existing `toucan.project` and offers to open or overwrite.
- **Statusbar language selector** — Click the language code in the statusbar to switch the active language via popup menu.
- **Provider management tests** — 11 new tests covering provider settings (defaults, add/remove, save flush, schema fields).

### Changed
- **Pre-Translate dialog** — Redesigned with dual-panel layout (config left, preview right), dropdown provider selector, compact options, Fluent-styled table (no more Vista GridView).
- **Inspector panel** — Tabs now use custom Fluent-styled underline indicator (accent-colored bottom border on selected tab). Removed native TabControl chrome.
- **About page** — Redesigned to match Files App style: app card with Copy button, Help & support links, Open source section.
- **New Project dialog** — Framework tiles reduced (80×60), project name/folder moved to top, Step 2 made compact with inline add buttons.
- **Suggested Languages** — Redesigned with card-style items (Globe icon, Fluent Add button).
- **Mode selector** — Made more compact (26px height, 11px font, reduced padding).
- **Zen mode** — Card now has `MinWidth="500"`, `MaxWidth="900"`, proper card styling with rounded corners.
- **Focused Editor mode** — Now functional: shows single item with navigation bar (↑/↓/exit) when activated.
- **Pagination buttons** — Disabled state uses opacity instead of opaque background (dark mode fix).
- **Language Prompt dialog** — Width set to 400px (was stretching full screen).

### Fixed
- **Pretranslation mock output** (`[provider/lang]`) — Root cause: `IProviderSettingsService` was not being passed to `PreTranslateViewModel`. Empty API keys were being encrypted/stored. Fixed both the DI wiring and the save/load logic to skip empty secrets.
- **Duplicate translations** — When root folder has both `en/` dirs and a nested `locales/en/` dir, the loader now skips the nested `locales/` to prevent duplicates.
- **"Showing N of M items"** — Removed redundant text from statusbar (pagination already shows this).
- **Provider settings button cropped** — Replaced narrow `Width="30"` button with proper `ui:Button` + SymbolIcon.
- **Project Languages showing only 1** — Now loads discovered languages from actual files, not just manifest.
