---
title: "Verification records"
status: active
updated: 2026-11-26
summary: "Machine-readable records of tests and hand checks per commit and OS, written by tools/record-evidence and required by the release checklist."
---
# Verification records

Each file here records one check of one commit on one machine, so a result run on a Windows PC or a Linux laptop counts for a release made on the Mac. Records are written and committed by `tools/record-evidence.sh` (macOS, Linux) or `tools/record-evidence.ps1` (Windows) and follow [schema.json](schema.json). `tools/check-evidence.py` reads them for the [release checklist](../release-checklist.md).

Layout: `docs/evidence/<version>/<os>-<check>-<time>.json`. The older `v0.24-platform-memory.json` and `v0.25-readiness.json` are free-form history.

## Recording

Automated tests (runs the whole solution in Release, then records the counts):

```bash
tools/record-evidence.sh tests
```

```powershell
pwsh tools/record-evidence.ps1 tests
```

A hand check, after doing it:

```bash
tools/record-evidence.sh check screen-reader --result passed --packaged --notes "VoiceOver: Sources panel, plan and conflict dialogs"
```

Options: `--notes TEXT`, `--packaged` (the check used an installed build), `--no-commit` (write the file only), `--allow-dirty` (record although the tree has uncommitted changes; the record says so and the release check ignores it).

The record names the checked commit. Committing the record makes a new commit on top; the release check accepts it as long as only `docs/evidence/`, the evidence tools (`tools/record-evidence.*`, `tools/check-evidence.py`) and Markdown files changed in between (no build or test reads a `.md` file), so the release can be tagged on the commit that holds its own evidence.

## What counts

- `environment` is `native`, `vm`, `ci` or `wsl`. WSL counts for Linux tests but not for packaged builds or the keyring.
- A record with `dirty: true` does not count for a release.
- `recordedBy` is the git user name of whoever ran it; no e-mail address is stored.

`recordedAt` must be `yyyy-MM-ddTHH:mm:ssZ`; the check ignores a record with any other form, since the latest record decides.
