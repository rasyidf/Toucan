# Known Issues & Unfinished Features

Last updated: 2026-07-02 (v0.15.0)

## Fixed Bugs (Summary)

All 22 bugs from v0.14.1–v0.14.2 have been resolved. See CHANGELOG.md for details.

## Previously Unfinished Features — All Resolved in v0.15.0

- ~~U1. OptionsViewModel stubs~~ → BrowseSourceRoot/BrowseSourceEditor/ConfigureLanguageCodes wired
- ~~U2. IUnsavedChangesHandler never wired~~ → Prompts save/discard/cancel on close
- ~~U3. FilterUsedKeys/FilterUnusedKeys don't filter~~ → Now filters based on source scan
- ~~U4. ShowMachineTranslations is visual-only~~ → Filters to unapproved+filled items
- ~~U5. Keybinding shortcuts page display-only~~ → Info banner added, customization deferred to v1.1

## Current Limitations (Not Bugs)

- Performance not profiled yet (Phase 5 of UI revamp deferred)
- Side panels are hardcoded (Explorer left, Inspector right) — extensible panel system planned for v0.16
- No auto-updater — planned for v1.1
- Keybinding customization — display-only reference for now

## Next: v0.16 — Panel Extension System

See [docs/todos/panel-extension-plan.md](../todos/panel-extension-plan.md) for the full plan.
