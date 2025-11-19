# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Added
- e4a648a — Refactor application structure and implement StatusBar features: added `StatusBarService`, `StatusBarViewModel`, `StatusBarView`; integrated status updates and Git stats in the UI; added `ToolBarView` and new project/translation commands.
- 91a862f — feat: Introduce `IProjectService` and `ISaveStrategy` interfaces: new service abstractions for project management and save strategies.

### Changed
- 8906a0d — Refactor and enhance UI/UX: general UI/UX improvements and added support for bulk actions.
	- Refactored `TreeItemtoListItemConverter` to handle non-generic `IEnumerable` and improved `DisplayKey` formatting.
	- Introduced `IBulkActionService` and `BulkActionService` for pre-translation workflows and translation statistics generation.
	- Updated `Toucan.csproj` (project title change, NuGet package upgrades, and removal of unused dependencies).
	- Enhanced `MainWindowViewModel` with new properties and commands to control UI visibility and bulk actions.
	- Added `OptionsViewModel` to centralize application options and migrated logic from `OptionsDialog`.
	- Refactored views and menus: `LanguagesView.xaml`, `MainMenu.xaml`, `ResourcesView.xaml` — improved styling and consistency.
	- Enhanced `MainWindow.xaml` layout with `ui:TitleBar` and added a `SearchFilterTextbox` control.
	- Updated `OptionsDialog` and `TranslationItemView` for better organization, styling, and maintainability.
	- General UI/UX improvements: consistent usage of `ui:SymbolIcon`, alignment fixes, and better maintainability across views.

### Documentation
- 22b4814 — Revise README for improved clarity and detail: updated project README content for clarity.

 