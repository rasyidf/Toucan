---
name: doc-status
description: "Keep Toucan docs skimmable: every doc carries a YAML status header (status, progress, summary), and finished or obsolete docs move to docs/archive/ instead of being deleted. USE FOR: adding or editing any doc under docs/ (including docs/specs), packaging/ or Toucan.Core/; finishing a plan or spec; retiring a doc; auditing what is current. DO NOT USE FOR: README.md, CHANGELOG.md, CODE_OF_CONDUCT.md, the NuGet package README, .agents/ or .kiro/skills/ files."
---

# Doc status headers and archive

Readers should learn what a doc is, how far along it is and whether to trust it from the first ten lines, without opening it. `docs/INDEX.md` lists every doc from those headers.

## Header

Every doc starts with this block (nothing above it):

```yaml
---
title: Short name
status: active
progress: "36/40 tasks"
updated: 2026-10-07
summary: One or two sentences. What it covers and what state it is in, in plain words.
---
```

| Field | Rule |
|-------|------|
| `title` | Required. Same as the H1. |
| `status` | Required. One of the values below. |
| `progress` | Optional. Only for `in-progress` and `planned` docs and finished plans: `"n/m unit"` or a short phrase. Count real checkboxes or shipped items, do not guess. |
| `updated` | Required, `YYYY-MM-DD`. Change it whenever the content changes, not for typo fixes. |
| `summary` | Required. Under 200 characters. Say what is done and what is left. |
| `archived`, `reason`, `superseded_by` | `done` and `deprecated` docs only. `archived` and `reason` are required; `superseded_by` is a path when something replaced it. |

### Status values

| Status | Meaning | Location |
|--------|---------|----------|
| `active` | Describes the app as it is now; kept accurate. | in place |
| `in-progress` | A plan or spec being worked on. Needs `progress`. | in place |
| `planned` | Agreed but not started. | in place |
| `reference` | Stable background (formats, schema, brand). Changes rarely. | in place |
| `done` | A plan or spec that shipped. History only. | `docs/archive/` (specs: `docs/specs/<name>/`) |
| `deprecated` | Obsolete: the thing it describes was removed or abandoned. File name ends in `-deprecated.md`. | `docs/archive/` (specs: `docs/specs/<name>/`) |

## Workflow

1. **New doc**: add the header, status `planned` or `in-progress` for plans, `active` for descriptions.
2. **Doc changes**: update `summary`, `progress` and `updated` in the same edit. If a task list moved, recount.
3. **Plan finished or doc obsolete**: set `status` to `done` or `deprecated`, then archive. Never delete.
   ```bash
   python3 .agents/skills/doc-status/scripts/docs_status.py archive docs/todos/some-plan.md --reason "Shipped in v0.20.0"
   ```
   The script stamps `archived` and `reason`, then `git mv`s the file into `docs/archive/`. Specs (`.kiro/specs/<name>`) go to `docs/specs/<name>/` and stay there whatever their status. A `deprecated` doc is renamed with a `-deprecated` suffix (`foo.md` becomes `foo-deprecated.md`).
4. **Fix links** to the moved file: `grep -rn "some-plan.md" --include='*.md' --include='*.html' --include='*.cs' .`. Point live docs at `docs/archive/...`, or drop the link if the target no longer matters. Leave CHANGELOG entries that describe history, but fix links a reader would click.
5. **Verify and refresh the index**:
   ```bash
   python3 .agents/skills/doc-status/scripts/docs_status.py check
   python3 .agents/skills/doc-status/scripts/docs_status.py index
   ```
   `check` exits 1 when a header is missing or invalid, a `done`/`deprecated` doc is outside the archive, a `deprecated` doc lacks the suffix, or a doc in `docs/archive/` has a live status.

Other commands: `list [--status in-progress]` prints a one-line view of every doc.

## Trackers hold open work only

`docs/todos/` files and `docs/known-bugs.md` list what is *not* done. Finished work is removed, not ticked:

| Fact | Lives in |
|------|----------|
| Planned work | `docs/todos/future-roadmap.md` (open `- [ ]` items) |
| Open bugs and gaps | `docs/known-bugs.md` (one ID each; the roadmap links the ID) |
| What shipped, by release | `CHANGELOG.md` |
| What the app can do now | `docs/completed-features.md` (shipped only) |

`check` fails when a tracker contains `- [x]`, or `completed-features.md` contains `- [ ]`. Update `progress` to the open-item count.

## Judging status

- A plan whose items are all shipped (check `CHANGELOG.md` and the code) is `done`, even if its checkboxes were never ticked.
- Optional tasks (`- [ ]*` in spec) do not block `done`; say so in `summary`.
- A doc about the removed WPF app is `deprecated` unless it is a deliberate historical record, which is `done`.
- When a doc's claims no longer match the code, fix the doc or say what is stale in `summary`. Do not mark it `active` as if it were correct.

## Scope

Checked: everything under `docs/` (except `docs/INDEX.md`), `docs/specs/` (inside `docs/`), `Toucan.Core/ARCHITECTURE.md`, `packaging/README.md`. Not checked: `README.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `Toucan.Plugins.Abstractions/README.md` (packed into the NuGet package, where a header would show), skill files, `build/`, and non-Markdown files such as `docs/roadmap.json`. Edit scope in `SCOPE` at the top of the script.
