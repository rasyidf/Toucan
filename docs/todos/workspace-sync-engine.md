---
title: Workspace sync engine tracker
status: in-progress
progress: "2 open tasks"
updated: 2026-10-29
summary: "Core engine, targeted saves, destination choices, navigator UI, conflict presentation, lifecycle gates, save-scope declarations, and headless CLI saves are implemented. 2 tasks remain: format migration and integration verification."
---
# Workspace sync engine tracker

Design and acceptance criteria: [Workspace sync engine design](../specs/workspace-sync-engine/design.md). Related release sequencing: [future roadmap](future-roadmap.md).

This tracker contains open work only. Remove shipped items, recount `progress`, and update `updated` and `summary`. Record shipped behavior in the changelog and completed-features document. Archive the tracker and design when the scope ships.

## Phase 6: Source lifecycle and migration

- [ ] Migrate built-in formats beyond JSON and namespaced JSON to document reading and writing, each with fidelity fixtures. Save scopes are declared and a guard test pins the verified formats. ARB is the closest candidate, but document metadata moves comments from `@key.description` into `.toucan` sidecars, so it needs a product decision first. Renames and deletions must carry `@key` metadata with the message.
- [ ] Add integration coverage for autosave ticks, plugin `AddKey` edits in ambiguous layouts (today the save reports "Select an explicit destination"), and approval policy on destination edits against document projects. Crash recovery of destinations and removed languages, and headless CLI saves, are covered.
