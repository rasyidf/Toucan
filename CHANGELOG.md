# Changelog

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
