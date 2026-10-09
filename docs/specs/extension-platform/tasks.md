---
title: "extension-platform — tasks"
status: in-progress
progress: "5/8 steps done"
updated: 2026-10-09
summary: "v0.23 work order: registration/activation split, command registry, desktop contributions, host services, workspace API, compatibility checks, SDK harness and external sample connector, migration of built-ins."
---
# Tasks: extension platform (v0.23)

Source of truth for scope is [future-roadmap.md](../../todos/future-roadmap.md#v023-extension-platform). Each step leaves `dotnet test Toucan.CrossPlatform.slnx` green. Built-in commands and panels move onto the new paths as each piece lands (step 8 is a sweep, not a final rewrite).

- [~] 1. Registration vs activation
  - [x] 1.1 Registration without network access; activation per project or connection with its own session
  - [x] 1.2 Cancel and dispose on project close; separate registration and activation failure reporting
  - [x] 1.3 Explicit application, workspace, connection and panel lifetimes
- [x] 2. Command registry
  - [x] 2.1 Stable IDs, localizable titles and categories, availability conditions, async execution with cancellation and progress
  - [x] 2.2 Default shortcuts with user overrides; replace the static table in `KeybindingService`
  - [x] 2.3 Menu, toolbar, context-menu and command-palette contributions; hidden / unavailable / disconnected / unlicensed states
- [x] 3. Desktop contributions (`Toucan.Plugins.Avalonia`, so the CLI loads no UI assemblies)
  - [x] 3.1 Registered side panels, panel toolbars, settings pages, dialogs, inspector sections, editor actions in place of the `MainWindow` switches
  - [x] 3.2 Narrow workspace context for plugin view models; theme, icon, localization and accessibility conventions; shared Avalonia assemblies in the loader
- [x] 4. Host services: plugin settings and storage outside the plugin directory, connection secret references, background operations, notifications, redacted diagnostics, typed configuration fields with migrations; document that plugins are not sandboxed
- [x] 5. Workspace API: snapshots and edit transactions for values, comments, review state and keys via normal undo, dirty tracking, validation and persistence
- [ ] 6. Compatibility checks: host requirements, desktop-contract version, platform support, configuration migrations, actionable messages
- [ ] 7. SDK test harness and a sample connector in a separate project that references only published SDK packages
- [ ] 8. Sweep: move remaining built-in commands and panels onto the registries; update `docs/plugins.md`, architecture docs, changelog

Notes from step 1: `IPluginActivator`, `PluginLifetime` and the `activation` capability are in Abstractions (plugin API 1.1; 1.0 plugins still load). `PluginActivationService` in Core runs activators per application, workspace and connection; `ProjectLifecycleService` opens and closes the workspace scope. Connection scopes are driven by `OpenConnectionAsync` and have no caller until the connector sample (step 7). Panel lifetime moves to step 3 with the desktop contract.

Notes from step 2: `ICommandRegistry` (Core) holds built-in and plugin commands; the Avalonia `KeybindingService`, menus, palette and the Shortcuts settings page read from it. Built-in IDs start with `toucan.`, plugin IDs with the plugin ID. User shortcut overrides live in `AppOptions.CustomShortcuts`. Placements for toolbar and context menu are in the model but rendered in step 3. `GetState` distinguishes hidden, unavailable, disconnected and unlicensed; handlers report the last two.

Notes from step 3: `Toucan.Plugins.Avalonia` (desktop contract 1.0) holds the contribution types; a plugin names its UI assembly in `plugin.json` under `desktop` and the CLI never loads it. `DesktopPluginLoader` loads it through the plugin's own load context, with Avalonia, FluentAvalonia and the contract shared from the host, and applies the registrations all or nothing. `DesktopContributions` is the one place `MainWindow` asks for panels and toolbars; the eight built-in panels register there like a plugin's. Plugin settings appear on the Plugins page (not as new pages in the sidebar, which are index-based). Toolbar placement for commands outside panels and plugin-defined dialogs beyond a hosted control are not built. Theme, icon, localization and accessibility conventions are in `docs/plugins.md`.

Notes from step 4: `IPluginServices` (UI-free, one per plugin) carries storage, typed settings with migrations, secrets, notifications, background operations and redacted diagnostics. A host calls `UsePluginServices` once its container is built; the desktop app and the CLI do. The desktop app shows toasts, a background-operations indicator and plugin status bar items, builds a settings form per schema, and has Help → Copy Diagnostics.

Notes from step 5: `IWorkspaceApi` (snapshots and all-or-nothing edit transactions) is defined UI-free in the abstractions. `WorkspaceEditPlanner` (Core) dry-runs an edit on a copy; the desktop app applies the planned steps through the editor's own primitives (`MainWindowViewModel.Workspace.cs`). `IUndoRedoService.BeginGroup` makes a bulk edit one undo step (the v0.24 "bulk operations undo as one step" task can reuse it). A host with no open project provides no `IWorkspaceBackend`; the CLI has none yet, and the SDK harness (step 7) supplies an in-memory one.

## Completion gate

The externally built sample connector works on desktop and in the CLI; its command is in the palette and a menu, supports shortcut reassignment, and becomes unavailable when its workspace closes; a plugin batch edit behaves like a native edit through undo, save failure, recovery and reopen.
