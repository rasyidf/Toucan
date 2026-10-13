---
title: "editor-comfort — tasks"
status: planned
progress: "0/7 steps"
updated: 2026-10-13
summary: "v0.24 work order: bulk undo grouping, focus-stable inline + side-by-side editing, notification history flyout, predictable review loop, inspector provenance, first-run setup and empty states, and an accessibility pass."
---
# Tasks: editor comfort and onboarding (v0.24)

Source of truth for scope is [future-roadmap.md](../../todos/future-roadmap.md#v024-editor-comfort-and-onboarding); design is [design.md](design.md). Each step leaves `dotnet test Toucan.CrossPlatform.slnx` green and adds its own tests. Ordered lowest-risk-highest-value first: the bulk-undo step reuses v0.23 plumbing and clears a tracked bug, so it leads.

- [ ] 1. Bulk operations undo as one step (R3, A3)
  - [ ] 1.1 Route `BulkOperationService` / `BulkActionService` / pre-translate / replace-all through the editor edit primitive so each change records on the undo stack
  - [ ] 1.2 Wrap each operation in `IUndoRedoService.BeginGroup`; a no-op operation records no step
  - [ ] 1.3 Tests: N-item operation undoes/redoes in one step and survives save/reopen; remove APP-04 from known-bugs and add a CHANGELOG Fixed line
- [ ] 2. Focus-stable inline editing and side-by-side (R2, A2)
  - [ ] 2.1 Keep focus and caret through commit, validation and suggestion refresh (stabilize edited-item identity in the virtualized list)
  - [ ] 2.2 Side-by-side primary/target view for the selected item
  - [ ] 2.3 Content-sized multiline target field with character and word count
- [ ] 3. Notification history (R6, A6)
  - [ ] 3.1 Flyout anchored to the status-bar badge listing `INotificationCenter.History` newest-first with time, severity, source
  - [ ] 3.2 Badge counts unseen notifications (separate from the empty-translation count); progress / cancellation / completion are distinguishable
  - [ ] 3.3 Actionable entries navigate (save failure, validation findings, integration failure); remove APP-03 from known-bugs and add a CHANGELOG Added line
- [ ] 4. Predictable review loop (R4, A4)
  - [ ] 4.1 Approve / accept-suggestion / next retain focus on the working item and advance deterministically by keyboard
  - [ ] 4.2 Approve-and-advance lands focus on the next editable field; reuse Focused/Zen J/K navigation
- [ ] 5. Inspector: source, target and provenance (R5, A5)
  - [ ] 5.1 Inspector sections for source value, parsed placeholders, description and source-code references
  - [ ] 5.2 Suggestion provenance badge (TM / machine / AI); consistent placeholder highlighting
- [ ] 6. First-run setup and empty states (R1, A1)
  - [ ] 6.1 Add default-language and theme steps to onboarding; gate on a first-run flag in `AppOptions`
  - [ ] 6.2 Actionable empty states for no-project and no-keys, bound to existing commands
  - [ ] 6.3 Dismissible one-time feature tour
- [ ] 7. Accessibility and layout pass (R7, A7)
  - [ ] 7.1 Keyboard focus order and `AutomationProperties.Name` coverage (headless checks where possible)
  - [ ] 7.2 Themes, narrow widths and text scaling via the screenshot harness; RTL target content
  - [ ] 7.3 Fix issues found in the 100-string gate flow

## Completion gate

A user can work through 100 strings without losing their place or needing a mouse for routine actions; accessibility and layout issues found in that flow are resolved. The bulk-undo and notification-history steps also close APP-04 and APP-03 in [known-bugs.md](../../known-bugs.md).
