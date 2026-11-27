---
title: "Compatibility, migration and downgrade policy"
status: active
updated: 2026-11-26
summary: "What Toucan 1.0 freezes (plugin API, project and user files, sync state, source protocol), how files move forward, and what an older Toucan does with a file from a newer one."
---
# Compatibility, migration and downgrade policy

Toucan 1.0 freezes the contracts below. Within 1.x they only grow: fields, members and endpoints may be added, nothing is removed or changes meaning. A change that an older build would misread raises that file's version, and older builds then refuse the file instead of overwriting it.

## Frozen contracts

| Contract | Where | Version marker | Guarded by |
|---|---|---|---|
| Plugin API | `Toucan.Plugins.Abstractions` 1.3, `Toucan.Plugins.Testing` 1.3, `Toucan.Plugins.Avalonia` 1.0 | package version, `PluginApi.Current` | `docs/api/*.txt` baselines, `PublicApiTests` |
| Project manifest | `toucan.tproj` | `manifestVersion` (1; absent means 1) | [toucan.tproj.schema.json](toucan.tproj.schema.json), `ContractFileTests` |
| Workspace sidecars | `.toucan/workspace-identities.json`, `.toucan/entries/*.json` | `SchemaVersion` (1) | `WorkspaceMetadataCodec` |
| Review history | `.toucan/review-history.jsonl` | one JSON event per line; unknown fields ignored | `ReviewHistoryTests` |
| Accounts | `accounts.json` in the user's Toucan folder | `version` (1) | `ContractFileTests` |
| Sync state | `.toucan/local/sources/<connection>/baseline.json`, `journal.json` | `Schema` (1) | `ContractFileTests`, `SourceMigrationTests` |
| App settings | `settings.json` in the user's Toucan folder | none; unknown settings are kept | `AppOptions` |
| Source protocol | `/v1` REST contract | path prefix | [source-protocol.md](source-protocol.md), the conformance suite |

The BabelEdit-shaped manifest written by the BabelEdit converters is a separate, older contract described by [toucan.project.schema.json](toucan.project.schema.json); it is read for import and not frozen further.

## Plugin API

- Within 1.x: new types, new members with default implementations on interfaces, new optional parameters only through new overloads, and new enum values at the end. Plugins built against any 1.x package keep loading.
- Not within 1.x: removing or renaming anything public, changing a signature, or changing what a member means. Such a change is 2.0.
- Every change to the public surface updates the baseline in `docs/api/` in the same commit (`TOUCAN_UPDATE_DOCS=1`), bumps the package's minor version and `PluginApi.Current`, and gets a changelog line.
- Some contract types live in `Toucan.Core.*` namespaces for historical reasons. They are part of the frozen surface under those names.

## Files: moving forward

- Toucan reads every file version it ever wrote and upgrades it in memory. It writes the new version only when it saves that file for another reason, so opening a project never rewrites it.
- Fields added within a version are optional. An older build ignores them and, for `toucan.tproj` and `settings.json`, keeps them when it saves.
- Projects that used the full-rewrite save move to document saving (only edited entries change) when Toucan recognizes their framework, the languages they saved approvals for match the files' languages, and each language has one file. Otherwise they keep saving as before, so approvals and comments stay attached. Comments that older versions kept beside the files move into the file on the first save for formats that store comments themselves (ARB).
- Saving keeps each file's line endings, also for files an older version or another OS wrote; new files follow the files next to them (see [formats](formats.md)). Toucan before 1.0 wrote some formats and its own files with the newline of the machine that saved them, so a project saved on both Windows and macOS may have mixed files; each keeps what it has.
- Opening an older project upgrades it in memory and writes nothing, and it opens with nothing to save. The exception is a 0.23 project whose language files were saved twice: it opens merged and with changes, because the merged values are not in the original files until it is saved.

## Files: going back (downgrade)

An older Toucan never overwrites a file a newer Toucan wrote in a format it does not understand:

| File | What an older build does |
|---|---|
| `toucan.tproj` with a higher `manifestVersion` | Refuses to open the project and says to update Toucan. |
| `.toucan/` sidecars with a higher schema | Refuses to open the project and says to update Toucan. |
| `accounts.json` with a higher `version` | Shows no accounts and refuses to change them; the file stays as it is. |
| Sync baseline or journal with a higher schema | Sets the file aside as `*.corrupt-<time>` and plans the next sync as a first sync, which asks before merging anything. |
| `settings.json` | Uses the settings it knows and keeps the rest. |

A damaged file is never treated as missing. `toucan.tproj` is reported and left untouched; `accounts.json`, `settings.json` and sync state are set aside as `*.corrupt-<time>` before a new one is written. Credentials stay in the system keychain or secret store.

Before trying an older version on a real project, commit it or copy the folder: downgrade is protected against overwrites, not tested as a supported workflow.

## Source protocol

`/v1` is frozen. Responses may gain optional fields, and clients must ignore what they do not know. New operation types appear only behind a new `permissions` flag. A breaking change gets a new path prefix (`/v2`) and Toucan keeps speaking `/v1`.

## Supported upgrade paths

Projects and settings from 0.23.0, 0.24.0, 0.25.0 and 0.26.0 open in 1.0 with their approvals, comments, review state and connections. The milestones 0.27 to 0.29 shipped inside 0.26.0. Fixtures written by the real code of those releases (JSON, i18next, ARB, Java `.properties`, YAML and PO projects, and user settings) are in `tests/fixtures/releases/`; `ReleaseMigrationTests` opens each one and compares it with what that release itself read. How they were made is in `tools/release-fixtures/`.

YAML and `.properties` projects saved by 0.23 also wrote each language to a second file at the project root (`en.yaml`, `en.properties`), sometimes with the edits only there. They open with the copies merged into the original files: where the values differ, the copy's value is kept, because 0.23 never wrote the originals. Opening lists every difference and writes nothing; the next save writes the merged files and moves the copies to `.toucan/backup/0.23-copies/<time>/`.
