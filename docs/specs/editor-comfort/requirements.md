---
title: "editor-comfort — requirements"
status: reference
updated: 2026-10-25
summary: "Implemented v0.24 editor comfort requirements. Native workflow and accessibility acceptance remain open; see acceptance.md."
---
# Requirements: editor comfort and onboarding (v0.24)

Implementation is merged into `main` for the [v0.24.0 release draft](../../release-notes/v0.24.0.md). This document preserves the design scope; [acceptance.md](acceptance.md) records executed checks and remaining gates.


Source of truth for scope is [future-roadmap.md](../../todos/future-roadmap.md#v024-editor-comfort-and-onboarding). This milestone makes a new user able to open a project and finish a keyboard-driven translation session comfortably. It is UI and workflow work on top of the v0.23 command registry, workspace API and undo groups; it does not change file formats, the project model, or translation engines.

**References:** APP-03 (no notification history) in [known-bugs.md](../../known-bugs.md). APP-02 (shortcuts not editable) and APP-04 (bulk operations not undoable) are listed there against earlier releases; this milestone finishes the workflow around the first and the undo grouping for the second. Accessibility items are new in this milestone.

## What exists today (do not rebuild)

- `IUndoRedoService.BeginGroup()` already makes nested edits one undo step; single value edits already record. Bulk operations do not yet open a group.
- `INotificationCenter` already keeps a 200-item history and raises `Notified`; the status bar only shows a count of empty translations and nothing opens on click.
- An `OnboardingViewModel` / `OnboardingDialog` exists from AI integration (asks whether to turn AI on). First-run language and theme are not part of it.
- Shortcuts are reassignable with conflict detection and reset (shipped in v0.23); this milestone depends on that, it does not re-add it.

## Requirements

### R1 — First-run setup and empty states
- The first launch offers default source language, theme (system/light/dark) and optional translation-engine credentials, in one dismissible flow that extends the existing onboarding; skipping it leaves working defaults and never blocks the app.
- An empty window (no project) and an empty project (no keys) show an actionable state (open/create/import, add a key) rather than a blank pane.
- A dismissible feature tour points out the editor, panels and command palette once; dismissing it is remembered.
- Acceptance: a fresh profile reaches an open, usable editor without reading documentation; the choices persist across restart.

### R2 — Comfortable editing
- Inline editing is reliable: the edited field keeps focus and caret through commit, validation and suggestion refresh.
- A side-by-side source/target view shows the primary language next to the target being edited.
- Target fields grow to their content (multiline) within a bound, and show a character and word count.
- Acceptance: editing a value never steals focus or loses the caret position, and multiline values are fully visible without a separate dialog.

### R3 — Bulk operations undoable as one step
- Copy to language, replace all, pre-translate, move to namespace and delete each undo and redo as a single step, through the same undo stack as a manual edit.
- A bulk operation that changes nothing records no undo step.
- Acceptance: running a bulk operation on N items and pressing undo once restores all N; redo reapplies all N; this holds through save and reopen.

### R4 — Predictable review workflow
- The edit → accept suggestion → approve → next-item loop keeps focus on the working item and moves predictably to the next, by keyboard alone.
- The relevant commands are in the registry with reassignable shortcuts and conflict detection (reusing v0.23); this requirement covers their focus and navigation behaviour, not the shortcut editor.
- Acceptance: a user approves and advances through items using only the keyboard, and focus lands where the next edit happens each time.

### R5 — Clearer source, target and provenance
- The editor and inspector make the source/target hierarchy, placeholder rendering, descriptions, source-code references and the origin of a suggestion (TM, machine, AI) legible at a glance.
- Acceptance: for a selected item a user can tell what the source says, which placeholders must survive, and where a suggestion came from, without opening another view.

### R6 — Notification history
- A history view, anchored to the status-bar badge, lists past notifications with time, severity and source, and supports actionable entries (go to the failed save, the validation findings, the integration failure).
- Progress, cancellation and completion are distinguishable; the badge reflects unseen items, not the empty-translation count.
- The view reads `INotificationCenter.History`; the backend is not rebuilt.
- Acceptance: a save failure, a validation summary and a cancelled operation each appear in the history with the right state, and the actionable ones navigate to the right place.

### R7 — Accessibility and layout
- Light and dark themes, narrow windows, OS text scaling, keyboard focus order, screen-reader labels on controls, and right-to-left target content are correct on Windows, macOS and Linux.
- Acceptance: the 100-string flow in the completion gate has no unreachable control by keyboard, no unlabeled control for a screen reader, and no broken layout at a narrow width or large text size; issues found are fixed.

## Completion gate

A user can work through 100 strings without losing their place or needing a mouse for routine actions; accessibility and layout issues found in that flow are resolved. Each requirement leaves `dotnet test Toucan.CrossPlatform.slnx` green.

## Out of scope

Glossary and message-aware validation (v0.25), review-state tracking across source changes (v0.26), content sources and sync (v0.27+). No new file formats or translation engines.
