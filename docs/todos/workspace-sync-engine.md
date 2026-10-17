---
title: Workspace sync engine tracker
status: in-progress
progress: "12 open tasks"
updated: 2026-10-17
summary: "Core engine, desktop document saves/export, metadata, navigation projections, and versioned watching are implemented. 12 tasks remain for UI, recovery, connection lifecycle, and migration."
---
# Workspace sync engine tracker

Design and acceptance criteria: [Workspace sync engine design](../specs/workspace-sync-engine/design.md). Related release sequencing: [future roadmap](future-roadmap.md).

This tracker contains open work only. Remove shipped items, recount `progress`, and update `updated` and `summary`. Record shipped behavior in the changelog and completed-features document. Archive the tracker and design when the scope ships.

## Lifecycle integration gates

These gates apply throughout the phases; resolve ownership and routing contracts before engine extraction or source adapter implementation.

- [ ] Map engine/adapter resources to Application, Workspace, and Connection scopes with one disposal owner; keep registration free of workspace side effects.
- [ ] Activate configured source connections after Core workspace publication; unwind connection initialization failures and support explicit remote loading state.
- [ ] Integrate source connection replacement with owned dirty entries while preserving unrelated workspace scopes and configuration identity.
- [ ] Verify source connection replacement, host shutdown ordering, overlapping project opens, and source-adapter failure recovery alongside the existing activation/close/reopen tests.

## Phase 4: Targeted persistence

- [ ] Add app destination choices for ambiguous new entries and moves. Separate create-only Save As/export planning and desktop integration are implemented.
- [ ] Restore draft identity checkpoints when no durable identity catalog exists, and verify restart recovery with custom IDs. Document/draft identity persistence, desktop recovery, and journal schema 2 are implemented.

## Phase 5: Navigation and presenters

- [ ] Wire the namespace-first and file-first multilevel projections into the app navigator, including source origins for duplicate paths. Core projections and presenter-independent save-target tests are implemented.
- [ ] Adapt the current editor and preserve pending edits/selection across navigation or presenter changes.

## Phase 6: Source lifecycle and migration

- [ ] Present document-level dirty reload conflicts in the app. Core reloads clean documents, preserves dirty conflicts and history, and retains their original source versions.
- [ ] Implement source-session switching with pending-edit resolution and rejection of late old-session events.
- [ ] Declare legacy adapter save capabilities and migrate built-in formats incrementally with fidelity fixtures.
- [ ] Verify autosave, plugin edits, approval policy, crash recovery, and headless host integration against the engine.
