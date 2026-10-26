---
title: Workspace sync engine tracker
status: in-progress
progress: "3 open tasks"
updated: 2026-10-27
summary: "Core engine, document saves/export, metadata, navigator UI, versioned watching, conflict presentation, connection lifecycle gates, and session switching are implemented. 3 tasks remain for destinations and migration."
---
# Workspace sync engine tracker

Design and acceptance criteria: [Workspace sync engine design](../specs/workspace-sync-engine/design.md). Related release sequencing: [future roadmap](future-roadmap.md).

This tracker contains open work only. Remove shipped items, recount `progress`, and update `updated` and `summary`. Record shipped behavior in the changelog and completed-features document. Archive the tracker and design when the scope ships.

## Phase 4: Targeted persistence

- [ ] Add app destination choices for ambiguous or structured new entries, moves, and source language creation/removal. Document projects currently refuse the legacy language file writer. Separate create-only Save As/export planning and desktop integration are implemented.

## Phase 6: Source lifecycle and migration

- [ ] Declare legacy adapter save capabilities and migrate built-in formats incrementally with fidelity fixtures.
- [ ] Verify autosave, plugin edits, approval policy, crash recovery, and headless host integration against the engine.
