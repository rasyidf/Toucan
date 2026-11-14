---
title: Workspace sync engine tracker
status: in-progress
progress: "1 open task"
updated: 2026-11-05
summary: "Engine, desktop and CLI persistence and destination integration checks are implemented. Broader format migration remains open; existing formats retain safe legacy saves."
---
# Workspace sync engine tracker

Design and acceptance criteria: [Workspace sync engine design](../specs/workspace-sync-engine/design.md). Related release sequencing: [future roadmap](future-roadmap.md).

This tracker contains open work only. Remove shipped items, recount `progress`, and update `updated` and `summary`. Record shipped behavior in the changelog and completed-features document. Archive the tracker and design when the scope ships.

## Phase 6: Source lifecycle and migration

- [ ] Migrate built-in formats beyond JSON and namespaced JSON to document reading and writing, each with fidelity fixtures. Save scopes are declared and a guard test pins the verified formats. ARB is the closest candidate, but document metadata moves comments from `@key.description` into `.toucan` sidecars, so it needs a product decision first. Renames and deletions must carry `@key` metadata with the message.

Autosave, ambiguous plugin AddKey and destination approval-policy integration are covered by `WorkspaceDestinationTests`. Autosave no longer acknowledges unrelated external changes. See [v0.25 readiness](../specs/v0.25-readiness.md) (archived).
