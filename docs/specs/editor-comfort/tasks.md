---
title: "v0.24 editor comfort and onboarding"
status: in-progress
progress: "6/6 local delivery slices; 0/3 release gates accepted"
updated: 2026-10-25
summary: "Editor workflow, bulk undo, onboarding and notification history are implemented. Automated Windows checks pass; native and cross-platform release acceptance remains open."
---
# v0.24 editor comfort and onboarding

## Scope and acceptance

Deliver the seven editor/onboarding requirements in the roadmap using existing commands, history, settings and notification services. Keep unrelated v0.25+ feature work out of this release. Preserve the pending UI edits and committed compatibility implementation.

- [x] S1: Diagnose stalled test execution, stabilize current title-bar/menu/layout changes and establish a passing focused baseline.
- [x] S2: Make native copy, move, replace, pre-translate and approval batches consistent one-step undo operations, including metadata and identity. Keep cancel/conflict behavior intact.
- [x] S3: Source-first context, counts/provenance and registered accept/approve/next commands with retained target focus. Verify a 100-string keyboard session, page boundaries, RTL and generated restrictions.
- [x] S4: Extend optional first-run setup with language/theme choices, actionable empty states and dismissible workflow guidance.
- [x] S5: Show chronological notification history from the existing service, with actionable host failures, validation state and clear progress/cancellation outcomes.
- [x] S6: Reconcile inherited integration gaps; run full regression, fresh screenshots and packaging/material checks; update version/release docs and record native/cross-platform acceptance separately.

## Execution plan

1. Isolate test startup using a bounded standalone runner, starting with non-UI tests, then the UI shell. Diagnose rather than mute failures.
2. Implement and verify S1, then S2 with focused production-composition tests.
3. Implement S3–S5 through injectable view models and the command registry; add focused behavioral tests where state/persistence/navigation changes.
4. Review public/plugin contract implications and close integration gaps through existing capabilities. Avoid new public contracts where current services suffice.
5. Run the complete regression and CLI fidelity checks, source/material verification, documentation validation and layout captures. Verify platform/native workflows only in environments actually available.
6. Update this record and a release evidence report with commands, failures, results, commit/version scope and explicit remaining gates. Do not mark unexecuted platform rows accepted.

## Validation order

Repository xUnit guidance: focused behavior tests, shell/screenshot checks, full solution and CLI regression. MVVM review: state and mutations stay testable through commands/services. Doc-status: headers, live trackers and generated index. Final source/notices guard, whitespace/diff review and release-version consistency. Native keyboard, accessibility and claimed OS rows remain explicit gates.

## Execution log

2026-10-25: Began from bf7d51e plus 18 uncommitted UI/test files. Prior focused VSTest attempts stalled without results. Isolating the standalone runner before modifying implementation.

2026-10-25: S1–S5 implemented with direct xUnit v3 execution and workspace-local temporary files. See [acceptance evidence](acceptance.md) for results and remaining release gates.

## Outstanding release acceptance

- [ ] Native Windows input, accessibility and OS text scaling.
- [ ] Linux/macOS packaged workflows and compatibility M7c platform rows.
- [ ] External connector desktop/CLI and connection-management integration (APP-05).

2026-10-25: Local S6 verification completed: 1,396 tests passed (1,087 Core, 298 UI, 11 SDK), CLI compatibility/scanner checks passed, SDK packages and isolated NuGet-only consumer verified, 193 screenshots captured, and published source/notice hashes checked. These results do not accept the three release gates above. This was the pre-commit verification snapshot on release/v0.23.0.

2026-10-25: Shared dialog chrome, project-aware menus/palette and hidden start-screen rails completed. The accumulated editor and shell implementation is recorded together in Git history; native/platform acceptance remains pending.
