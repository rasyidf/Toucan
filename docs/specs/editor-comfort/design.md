---
title: "editor-comfort — design"
status: planned
progress: "0/7 areas"
updated: 2026-10-13
summary: "v0.24 design: extend onboarding and add empty states, make the editor keep focus and show side-by-side multiline fields, wrap bulk operations in an undo group, keep focus through the review loop, surface provenance in the inspector, add a notification history flyout over the existing center, and run an accessibility pass."
---
# Design: editor comfort and onboarding (v0.24)

Requirements: [requirements.md](requirements.md). This design maps each requirement to the existing code it extends, so the work is wiring and UI rather than new subsystems. Every change keeps `dotnet test Toucan.CrossPlatform.slnx` green.

## Principles

- Reuse the v0.23 seams: the command registry for anything triggerable, `IUndoRedoService.BeginGroup` for atomic edits, `INotificationCenter` for history. Do not add parallel systems.
- Keep Core UI-free. Editor comfort is almost entirely in `Toucan.Avalonia`; Core changes are limited to making bulk helpers record through the undo service.
- Prefer behaviour tests at the view-model level (headless Avalonia) over manual checks, as the existing editor tests do.

## A1 — First-run setup and empty states (R1)

- Extend `OnboardingViewModel` / `OnboardingDialog` with a default source language and theme step before the existing AI step. Persist through `IPreferenceService` (`AppOptions`) and the theme service already used by Settings.
- `IDialogService.ShowOnboardingAsync` already exists; the gate for showing it moves from "AI unset" to "first run" (a stored `HasCompletedOnboarding` flag in `AppOptions`).
- Empty states: add content to the start screen (no project) and the editor area (project with no keys) with buttons bound to existing commands (open, create, import, add key). No new commands.
- Feature tour: a lightweight overlay sequence shown once, gated by a stored flag; dismiss writes the flag.

## A2 — Comfortable editing (R2)

- Focus/caret retention: audit the edit-commit path in `MainWindowViewModel` and the editor card so a commit/validation/suggestion refresh does not rebuild the focused control. Where the list virtualizes, keep the edited item's identity stable.
- Side-by-side: a view mode that shows the primary-language value beside the target field for the selected item, reusing the existing editor card layout.
- Multiline + counts: target field grows to content within a max height; a character/word count label reads the field's text. Pure view/view-model, no model change.

## A3 — Bulk operations undoable as one step (R3)

- The bulk helpers (`BulkOperationService`, `BulkActionService`, pre-translate, replace-all) mutate `TranslationItem`s directly today and bypass the undo stack. Route them through the editor's edit primitive so each change is recorded, and wrap the whole operation in `using var _ = undo.BeginGroup();` so it is one step.
- An operation that changes nothing opens a group that records no edits, which `BeginGroup` already treats as no step.
- This is the same grouping the v0.23 workspace API uses for a plugin batch edit, so plugin and native bulk edits behave identically. Resolves APP-04.
- Tests: for each operation, apply to N items, assert one undo restores all N and one redo reapplies all N, and that the result survives save/reopen (mirror `WorkspaceApiTests` style).

## A4 — Predictable review workflow (R4)

- The approve/next/accept-suggestion actions are registry commands (v0.23). This area makes their handlers retain focus on the working item and advance selection deterministically (next untranslated / next in filter), so the keyboard loop never jumps.
- Reuse the existing Focused/Zen J/K navigation; ensure approve-and-advance lands focus on the next editable field.

## A5 — Source, target and provenance (R5)

- Inspector sections (the v0.23 inspector contribution model) show: source value, placeholder list parsed from the value, description, source-code references (from `ISourceCodeService`), and a suggestion-provenance badge (TM / machine / AI) carried on the suggestion.
- Placeholder rendering highlights `{name}`, `{0}`, `%s`, `:param` consistently with the existing validation rule's parser.

## A6 — Notification history (R6)

- Backend exists: `INotificationCenter.History` (200-item ring) and `Notified`. Add a flyout anchored to the status-bar badge that lists history newest-first with time, severity and source.
- `StatusBarService.ShowNotificationBadge(int)` currently takes the empty-translation count; add an unseen-notification count fed from `Notified`, and keep (or rename) the translation count separately so the badge means "unseen notifications".
- Actionable entries: a notification may carry a navigation target (save failure → the file/items, validation → Issues panel, integration failure → the plugin). Add an optional action to `PluginNotification` consumption in the host without changing the plugin contract shape where avoidable.
- Distinguish progress/cancellation/completion using the existing background-operation states surfaced by host services.

## A7 — Accessibility and layout (R7)

- Automated where possible: headless checks for keyboard focus order and that interactive controls have `AutomationProperties.Name`. Themed/visual checks reuse the `TOUCAN_TEST_SCREENSHOTS` harness (see [visual-review.md](../../visual-review.md)) at narrow widths and large text.
- RTL: verify target text flows right-to-left for RTL languages in the editor and inspector.
- Fix issues found in the 100-string gate flow.

## Risks

- Focus retention in a virtualized list is the most fragile area; isolate it with view-model tests and a minimal reproduction before touching the view.
- Changing what the status-bar badge counts can confuse existing tests; update them in the same step.

## Test strategy

Behaviour tests in `Toucan.Avalonia.Tests` for A2–A6 (focus retention, bulk undo, history flyout state, provenance), Core tests for the undo-group wiring of bulk helpers, and the screenshot harness plus manual keyboard/screen-reader pass for A7. The 100-string flow is the acceptance check.
