---
title: "Known Issues & Unfinished Features"
status: active
updated: 2026-10-11
summary: "Open issues for v0.22.0: no tracked format bugs (limits per format are in formats.md), release gaps, missing panels, and verification limits. Fixed bugs live in CHANGELOG.md."
---
# Known Issues & Unfinished Features

Current release: **v0.22.0** (preview). Last audited **2026-10-09** against the code on `release/v0.22.0`.

This file lists what is wrong *now*. Fixed bugs are not kept here: they are in [CHANGELOG.md](../CHANGELOG.md) (see [Where the old fixes went](#where-the-old-fixes-went)). When a bug is fixed, delete its row and section here and add a `### Fixed` line to the changelog `[Unreleased]` section. Planned work for an ID is linked from [the roadmap](todos/future-roadmap.md) (REL, APP, QA); the roadmap does not describe it again. The WPF app is gone from `main` (source: branch `legacy/wpf`), so nothing here is about WPF.

**How each issue was checked.** "Reproduced" means I ran the case through the real load and save strategies and the output is quoted. "From code" means the cause is visible in the source but I did not run it. Run the same cases again before fixing, and turn each repro into a test in `tests/Toucan.Core.Tests/Formats/`.

## Open issues

| ID | Severity | Area | Problem | Checked |
|----|----------|------|---------|---------|
| [REL-01](#rel-01) | Medium | Release | No release pipeline; every release is built by hand (cross-platform CI is finalized) | Reproduced |
| [REL-02](#rel-02) | Medium | Release | macOS app is ad-hoc signed and not notarized | From docs and script |
| [REL-03](#rel-03) | Low | Release | Update check is manual only: no check on startup, no download or install | From code |
| [REL-04](#rel-04) | Low | Release | Windows ships as a portable zip; no installer or MSIX in releases | From docs |
| [APP-01](#app-01) | Low | App | Source Control and Dictionary panels do not exist | Reproduced |
| [APP-02](#app-02) | Low | App | Keyboard shortcuts cannot be changed | Reproduced |
| [APP-03](#app-03) | Low | App | No notification history; the status-bar badge only shows a count of empty translations | Reproduced |
| [APP-04](#app-04) | Low | App | Bulk operations (copy to language, replace all, pre-translate, move, delete) cannot be undone | From code |
| [QA-02](#qa-02) | Info | Perf | Large-project performance has never been profiled | Not checked |

Severity: **High** loses or corrupts user data; **Medium** gives wrong results or blocks a release goal; **Low** is a gap or a cosmetic problem; **Info** is a known unknown.

Suggested order: release work first (REL-01), then the rest.

---

## Format bugs

None are tracked. What each format keeps and drops on save is in [formats.md](formats.md); the tests that guard it are `FormatFidelityFixtureTests`, `FormatStabilityTests`, `FormatBugRegressionTests` and `YamlFidelityTests` in `tests/Toucan.Core.Tests/Formats/`.

---

## Release and distribution

<a id="rel-01"></a>
### REL-01 — No automated release pipeline

- **Severity:** Medium · **Checked:** `.github/workflows/ci.yml` builds and tests on Windows, macOS and Linux for pull requests and pushes. CI is fixed and finalized. There is no release workflow.
- **State:** release builds come from `publish.ps1`, `packaging/build-macos-app.sh` and `dotnet publish` run by hand. v0.18.0 Linux tarballs were made by hand too. 
- **Impact:** CI protects against regressions, but release artifacts are still built manually and depend on the build machine.
- **Fix direction:** FG-01 and FG-16 in [docs/todos/future-roadmap.md](todos/future-roadmap.md): CI is complete; the remaining work is the v0.30 tag-triggered release workflow that builds all packages and attaches them to the GitHub release.

<a id="rel-02"></a>
### REL-02 — macOS app is not notarized

- **Severity:** Medium · **Checked:** README and `packaging/build-macos-app.sh`.
- **State:** the build is ad-hoc signed. The script already signs with a Developer ID and notarizes when `SIGN_IDENTITY` and `NOTARY_PROFILE` are set, but no such identity is configured.
- **Impact:** Gatekeeper blocks the first launch ("could not verify"). Users must use Open Anyway or clear the quarantine flag. This is documented in the README but costs installs.
- **Fix direction:** an Apple Developer ID and a notary profile stored as CI secrets (needs REL-01).

<a id="rel-03"></a>
### REL-03 — No auto-updater; the update check is manual

- **Severity:** Low · **Checked:** since v0.20.0, Settings → About has a release channel (Stable or Preview) and a Check for updates button (`UpdateService`, GitHub Releases API). Nothing checks on startup, and nothing downloads or installs.
- **Impact:** users who never open About stay on old builds, which matters while the formats above lose data.
- **Fix direction:** FG-02, planned for v0.24: check on startup (off / notify / auto-install), then download and apply. Depends on REL-01 and signing (REL-02).

<a id="rel-04"></a>
### REL-04 — Windows has no installer

- **Severity:** Low · **Checked:** README says "portable x64 zip; no installer yet".
- **State:** `installer.iss` (Inno Setup) and `packaging/Build-Msix.ps1` exist and refer to `Toucan.exe`, which is the Avalonia app's assembly name. I did not check whether they still work with the Avalonia build since the switch from WPF.
- **Fix direction:** run both against the current build, fix what breaks, then publish an installer with each release (part of REL-01).

---

## App gaps

<a id="app-01"></a>
### APP-01 — Source Control and Dictionary panels do not exist

- **Severity:** Low · **Checked:** `App.RegisterSidePanels` registers Explorer, Search, Issues, Source Code, Languages, Inspector, Translation, Memory. No panel for Git or glossary.
- **Correction:** the old text also listed "Translation" as missing. It exists (`machine-translation`, titled Translation).
- **Plan:** Git integration is FG-06 (v0.23), the glossary is FG-15 (v0.21).

<a id="app-02"></a>
### APP-02 — Shortcuts cannot be changed

- **Severity:** Low · **Checked:** Settings → Shortcuts lists `KeybindingService.GetDefinitions()` with no edit control and nothing is stored.
- **Fix direction:** per-user overrides in `settings.json`, a conflict check, a reset button, and the command palette showing the active shortcut (it already reads the definitions).

<a id="app-03"></a>
### APP-03 — No notification history

- **Severity:** Low · **Checked:** `StatusBarService.ShowNotificationBadge` is only fed the number of empty translations (`MainWindowViewModel.cs:266`). There is no `NotificationService` and nothing opens when the badge is clicked.
- **Fix direction:** a small service holding title, message, severity and time, a flyout anchored to the badge, and producers: save failures, validation summaries, plugin load errors, and (later) update availability.

<a id="app-04"></a>
### APP-04 — Bulk operations cannot be undone

- **Severity:** Low · **Checked:** `UndoRedoService` records single value edits (`Record(ns, language, old, new)`); `NotifyBulkValueChanges` and the bulk commands never record anything, so Undo after "copy to language" does nothing.
- **Fix direction:** group the edits of one bulk operation into a single undo step. Planned in v0.24.

---

## Quality

<a id="qa-02"></a>
### QA-02 — Performance not profiled

- **Severity:** Info · **Checked:** no. Loading depth, paging and `MaxItems` exist as mitigations, but there are no measurements for projects with tens of thousands of keys. Profile open, search and save before 1.0.

---

## Where the old fixes went

The earlier version of this file kept tables of fixed bugs (B1 to B11 and the v0.14 to v0.16 sets). All of them are already in [CHANGELOG.md](../CHANGELOG.md), so the tables were removed here.

| Old ID | What was fixed | Changelog entry |
|--------|----------------|-----------------|
| B1 | DiffMergeEngine merged items stayed dirty | 0.17.1 |
| B2 | AutoSaveService crash when disposed during a save | 0.17.1 |
| B3 | Double `DirtyStateChanged` (TOCTOU) | 0.17.1 |
| B4 | External reload updated UI collections off the UI thread | 0.17.1 |
| B5 | iOS `.strings` `\\n` corrupted | 0.17.1 |
| B6 | Java `.properties` line continuations truncated values | 0.17.1 |
| B7 to B11 | WPF-only UI fixes (Issues grouping, Search panel sizing, Source Code panel, Explorer foreground, status bar clicks) | 0.17.2 |
| QA-01 | Missing regression tests for B1 to B6 and the format edge cases | Unreleased (`Qa01RegressionTests`, `FormatFidelityFixtureTests`, `FormatStabilityTests`) |
| FMT-05 to FMT-09 | RESX language detection, XLIFF save losing source/notes/state, ARB metadata and region locales, YAML flat keys and `__self` | Unreleased |
| v0.14.1 to v0.16.1 | Earlier bug batches | 0.14.1, 0.14.2, 0.15.0, 0.16.1 |

B7 to B11 were fixed in the WPF app. Whether the Avalonia app has the same problems was not checked in this audit.
