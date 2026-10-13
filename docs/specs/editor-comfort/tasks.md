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
  - [x] 1.1 Route the value bulk operations (copy to language, replace all, pre-translate, text transforms) through the undo stack: snapshot values, mutate, record the diffs
  - [x] 1.2 Wrap each in `IUndoRedoService.BeginGroup`; a no-op operation records no step
  - [x] 1.3 Tests: N-item value operation undoes/redoes in one step (`BulkUndoTests`) and survives save/reopen; CHANGELOG Fixed line added
  - [ ] 1.4 Structural bulk operations (move to namespace, delete): extend the undo model with key add/rename/delete actions so they record and replay like value edits, then close APP-04 fully. The value-only `EditAction` model cannot represent them today.
- [ ] 2. Focus-stable inline editing and side-by-side (R2, A2)
  - [x] 2.1 Keep focus and caret through commit, validation and suggestion refresh: `Refresh` only raises `Value` when the model value actually changed
  - [x] 2.2 Side-by-side primary/target view: non-primary rows carry the primary value as `SourceText`, shown dimmed under the field
  - [x] 2.3 Content-sized multiline target field (`AcceptsReturn`, wrap, `MaxHeight`) with a character and word count (`CountSummary`)
- [ ] 3. Notification history (R6, A6)
  - [x] 3.1 Flyout anchored to the status-bar badge listing `INotificationCenter.History` newest-first with time, severity, source
  - [x] 3.2 Badge counts unseen notifications (separate from the empty-translation count); progress / cancellation / completion are distinguishable via severity labels
  - [x] 3.3 History backed by `NotificationHistoryViewModel`; APP-03 removed from known-bugs and a CHANGELOG Added line added. (Deep-link navigation for actionable entries reuses the existing notification action command; richer per-entry navigation can follow.)
- [ ] 4. Predictable review loop (R4, A4)
  - [x] 4.1 Approve and Next approves the current card's filled values and advances deterministically to the next card needing review (empty or unapproved), by index without wrapping; registered as reassignable commands
  - [x] 4.2 The loop reuses the Focused editor index/selection; Next Needing Review skips already-done cards. (Landing the OS text-caret on the next field is handled by the view binding to the advanced card.)
- [ ] 5. Inspector: source, target and provenance (R5, A5)
  - [x] 5.1 Inspector shows the source placeholders (parsed via `PlaceholderService`), per-language source/target values with state, description (comment) and source-code references
  - [x] 5.2 Provenance is surfaced where tracked: the Translation Memory section (TM origin) and the "machine" per-language state (`ChangeType.Suggestion`). Distinguishing machine from AI per value needs provenance on the model (`ChangeType` only has Suggestion/DirectEdit today) — tracked for v0.25's suggestion work.
- [ ] 6. First-run setup and empty states (R1, A1)
  - [x] 6.1 Default-language and theme steps added to onboarding (persisted via `IPreferenceService`); gated on `AppOptions.OnboardingVersion` (bumped to 2 so existing users see the new steps once)
  - [x] 6.2 Actionable empty states already present: the start screen (open/create/import/open-file + recents) and the editor "no keys match" state (clear filter / add key), both bound to existing commands — confirmed
  - [x] 6.3 Dismissible one-time feature tour (`FeatureTourViewModel`, overlay in `MainWindow`), gated by `AppOptions.FeatureTourSeen`, shown after first-run setup
- [ ] 7. Accessibility and layout pass (R7, A7)
  - [ ] 7.1 Keyboard focus order and `AutomationProperties.Name` coverage (headless checks where possible)
  - [ ] 7.2 Themes, narrow widths and text scaling via the screenshot harness; RTL target content
  - [ ] 7.3 Fix issues found in the 100-string gate flow

## Completion gate

A user can work through 100 strings without losing their place or needing a mouse for routine actions; accessibility and layout issues found in that flow are resolved. The notification-history step closes APP-03; the bulk-undo step closes APP-04 for value operations, with structural move/delete tracked in task 1.4.
