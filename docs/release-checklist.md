---
title: "Release checklist"
status: active
updated: 2026-11-26
summary: "What must be true and recorded before a Toucan release is tagged: tests and hand checks per OS for the release commit, checked by tools/check-evidence.py."
---
# Release checklist

A release is tagged only when `python3 tools/check-evidence.py` passes for the release commit. It reads the records in [docs/evidence](evidence/README.md); each one is written with `tools/record-evidence.sh` or `tools/record-evidence.ps1` on the machine that ran the check. A record counts for the release commit when it names that commit, or an earlier one that differs from it only in `docs/evidence/`, the evidence tools or Markdown files. Any other change, code or not, needs new records. The latest record for a requirement decides, so re-running after a failure replaces it.

## Before recording

- [ ] The version in `Directory.Build.props` is the version being released, and `CHANGELOG.md` has its section.
- [ ] `docs/known-bugs.md` has no open issue marked release-blocking, and the known-limitations page is current.
- [ ] The CI test matrix (`ci.yml`, platform `all`) is green for the release commit. Its job summaries and `test-results-*` artifacts are the CI copy of the results; the records below are the ones that count.

## Required records

| Requirement | Who runs it | Command | Covers |
|---|---|---|---|
| `tests@macOS`, `tests@Windows`, `tests@Linux` | anyone, on each OS | `tools/record-evidence.sh tests` (macOS, Linux), `pwsh tools/record-evidence.ps1 tests` (Windows) | The whole solution in Release. WSL counts for Linux. |
| `packaged-flow@<OS>` (packaged) | maintainer | `... check packaged-flow --result passed --packaged --notes "..."` | On the installed build: open, edit, validate, review, save and sync, close, reopen (V1-60). |
| `install-upgrade@<OS>` (packaged) | maintainer | `... check install-upgrade --result passed --packaged` | Clean install, upgrade from the previous release with settings, accounts and projects kept, uninstall (V1-45). |
| `gatekeeper@macOS` (packaged) | maintainer | `tools/record-evidence.sh check gatekeeper --result passed --packaged` | The downloaded DMG opens without a Gatekeeper warning (signed and notarized). |
| `keyring@<OS>` (native) | maintainer | `... check keyring --result passed` | A saved token survives restart: macOS Keychain, Windows DPAPI, Linux Secret Service. Not WSL. |
| `screen-reader@<OS>` | maintainer | `... check screen-reader --result passed --notes "VoiceOver/NVDA/Orca: screens checked"` | VoiceOver, NVDA or Narrator, Orca on the main editor and the Sources screens (V1-52). |

`<OS>` is each of macOS, Windows and Linux. Use `--notes` for what was checked and anything that looked wrong; record a failure with `--result failed` instead of not recording it.

## Tagging

1. Record the evidence above; each run commits its record.
2. Run `python3 tools/check-evidence.py` on the commit to tag. Every line must read `ok`.
3. Tag that commit and push the tag. The release workflow (V1-40) builds the packages and attaches them with checksums to a draft release; publishing the draft is a separate, manual step.

For a test-only check, such as a release candidate's first CI round, `python3 tools/check-evidence.py --only-tests` requires just the three test records.
