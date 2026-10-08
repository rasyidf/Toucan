---
title: "extension-platform — tasks"
status: in-progress
progress: "1/8 steps done, step 2 in progress (toolbar and context-menu rendering left for step 3)"
updated: 2026-10-09
summary: "v0.23 work order: registration/activation split, command registry, desktop contributions, host services, workspace API, compatibility checks, SDK harness and external sample connector, migration of built-ins."
---
# Tasks: extension platform (v0.23)

Source of truth for scope is [future-roadmap.md](../../todos/future-roadmap.md#v023-extension-platform). Each step leaves `dotnet test Toucan.CrossPlatform.slnx` green. Built-in commands and panels move onto the new paths as each piece lands (step 8 is a sweep, not a final rewrite).

- [~] 1. Registration vs activation
  - [x] 1.1 Registration without network access; activation per project or connection with its own session
  - [x] 1.2 Cancel and dispose on project close; separate registration and activation failure reporting
  - [x] 1.3 Explicit application, workspace, connection and panel lifetimes
- [~] 2. Command registry
  - [x] 2.1 Stable IDs, localizable titles and categories, availability conditions, async execution with cancellation and progress
  - [x] 2.2 Default shortcuts with user overrides; replace the static table in `KeybindingService`
  - [ ] 2.3 Menu, toolbar, context-menu and command-palette contributions; hidden / unavailable / disconnected / unlicensed states
- [ ] 3. Desktop contributions (`Toucan.Plugins.Avalonia`, so the CLI loads no UI assemblies)
  - [ ] 3.1 Registered side panels, panel toolbars, settings pages, dialogs, inspector sections, editor actions in place of the `MainWindow` switches
  - [ ] 3.2 Narrow workspace context for plugin view models; theme, icon, localization and accessibility conventions; shared Avalonia assemblies in the loader
- [ ] 4. Host services: plugin settings and storage outside the plugin directory, connection secret references, background operations, notifications, redacted diagnostics, typed configuration fields with migrations; document that plugins are not sandboxed
- [ ] 5. Workspace API: snapshots and edit transactions for values, comments, review state and keys via normal undo, dirty tracking, validation and persistence
- [ ] 6. Compatibility checks: host requirements, desktop-contract version, platform support, configuration migrations, actionable messages
- [ ] 7. SDK test harness and a sample connector in a separate project that references only published SDK packages
- [ ] 8. Sweep: move remaining built-in commands and panels onto the registries; update `docs/plugins.md`, architecture docs, changelog

Notes from step 1: `IPluginActivator`, `PluginLifetime` and the `activation` capability are in Abstractions (plugin API 1.1; 1.0 plugins still load). `PluginActivationService` in Core runs activators per application, workspace and connection; `ProjectLifecycleService` opens and closes the workspace scope. Connection scopes are driven by `OpenConnectionAsync` and have no caller until the connector sample (step 7). Panel lifetime moves to step 3 with the desktop contract.

Notes from step 2: `ICommandRegistry` (Core) holds built-in and plugin commands; the Avalonia `KeybindingService`, menus, palette and the Shortcuts settings page read from it. Built-in IDs start with `toucan.`, plugin IDs with the plugin ID. User shortcut overrides live in `AppOptions.CustomShortcuts`. Placements for toolbar and context menu are in the model but rendered in step 3. `GetState` distinguishes hidden, unavailable, disconnected and unlicensed; handlers report the last two.

## Completion gate

The externally built sample connector works on desktop and in the CLI; its command is in the palette and a menu, supports shortcut reassignment, and becomes unavailable when its workspace closes; a plugin batch edit behaves like a native edit through undo, save failure, recovery and reopen.
