---
name: conventional-commits
description: "Write Toucan commit messages and PR titles in Conventional Commits form. USE FOR: every commit, squash-merge title or PR title in this repository. DO NOT USE FOR: changelog entries or release notes (see doc-status and CHANGELOG.md)."
---

# Conventional commits

Every commit subject is `type(scope): summary`. The history uses this form (`feat(core): …`, `fix(cli): …`, `docs: …`); keep it consistent.

## Subject

- **type** (required):

  | Type | Use for |
  |------|---------|
  | `feat` | A new capability users or plugin authors can see |
  | `fix` | A bug fix |
  | `docs` | Docs, release notes, changelog, generated site pages |
  | `refactor` | Code change with no behavior change |
  | `perf` | Faster or lighter, same behavior |
  | `test` | Tests only |
  | `build` | Project files, packages, packaging scripts, SDK version |
  | `ci` | `.github/workflows` |
  | `chore` | Repository housekeeping: agent skills, editor config, cleanup |
  | `revert` | Reverting an earlier commit |

- **scope** (optional, lowercase): the area touched. Prefer `desktop` (Toucan.Avalonia), `core`, `cli`, `formats`, `providers`, `validation`, `plugins`, `sdk`, `packaging`, `release`, `agents`. Omit it when the change spans many areas.
- **summary**: imperative mood, lowercase first word, no trailing period, at most 72 characters for the whole subject. Say what changes for the user, not which file changed.
- **breaking change**: add `!` after the type or scope (`feat(sdk)!: …`) and a `BREAKING CHANGE:` footer saying what breaks and how to migrate. Plugin API (`Toucan.Plugins.Abstractions`) and project-file changes are the usual cases.

## Body

Blank line after the subject, then why the change was made and anything a reviewer cannot see from the diff. Wrap at 72 columns. Bullets are fine for several independent changes. Reference known-issue IDs (`APP-05`, `REL-02`) and roadmap groups (`FG-04`) where they apply.

## Footers

`BREAKING CHANGE:`, `Refs: #123`, `Closes: #123`, then any `Co-Authored-By:` lines last.

## One change per commit

Split unrelated work into separate commits, each with its own type: a fix plus a docs update is two commits unless the docs only describe that fix. Stage specific paths, not `git add .`.

## Examples

```
fix(desktop): draw window frame, move and resize under WSLg

WSLg wraps a bordered X11 window in a second frame and an offset
shadow, so under WSLg the window drops its border and handles move
and resize itself.
```

```
docs(release): record connector and live-AI decisions for v0.25
```

```
chore(agents): keep agent skills in .agents only
```
