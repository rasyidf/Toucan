---
title: Workspace sync engine tracker
status: in-progress
progress: "8 open tasks"
updated: 2026-10-27
summary: "Core engine, document saves/export, metadata, navigator UI, versioned watching, and document-level conflict presentation are implemented. 8 tasks remain for destinations, connection lifecycle, and migration."
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

- [ ] Add app destination choices for ambiguous or structured new entries, moves, and source language creation/removal. Document projects currently refuse the legacy language file writer. Separate create-only Save As/export planning and desktop integration are implemented.

## Phase 6: Source lifecycle and migration

- [ ] Implement source-session switching with pending-edit resolution and rejection of late old-session events.
- [ ] Declare legacy adapter save capabilities and migrate built-in formats incrementally with fidelity fixtures.
- [ ] Verify autosave, plugin edits, approval policy, crash recovery, and headless host integration against the engine.
