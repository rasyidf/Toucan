# StatusBar Features / TODO

This document collects future work for the new StatusBar and `StatusBarService`.

Planned major tasks:

- Git integration (large)
  - Detect repository (use libgit2sharp or Git CLI). Provide branch name and state.
  - Compute file changes (staged/unstaged/ignored) and show counts badge.
  - Provide commands to open Git view/diff, stage/unstage, commit, push/pull.
  - Integrate with Project-level `.gitignore` and show absolute paths where needed.
  - Add telemetry for status changes (not required initially).

- Stats / Diagnostics
  - Integrate simple parser for translation files to compute errors/warnings.
  - Wire into `StatusBarService.UpdateGitStats` for errors/warnings counts.
  - Optionally add ability to toggle 'Show Errors' or 'Show Warnings' via commands.

- Notification/Badge
  - Show number on top of Git or notification area.
  - Integrate with MessageService to show details when clicking badge.

- UX
  - Clickable areas for Branch/Notifications/Warnings.
  - Show tooltip with more details when hovering badges.

Notes:
- The `StatusBarService` is intentionally simple at the moment; we will add events or an IoC registration for richer updates later.
- Consider moving to a full event aggregator pattern once Git integration becomes large.

---

Add or expand this list while implementing features.
