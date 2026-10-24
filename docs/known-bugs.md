---
title: "Known Issues & Unfinished Features"
status: active
updated: 2026-10-25
summary: "Open source-build issues for the 0.24 preview: release distribution, missing panels, external connector acceptance and native/platform verification. Fixed issues live in CHANGELOG.md."
---
# Known Issues & Unfinished Features

Published release: **v0.22.0** (preview). Current source build: **0.24.0**, audited **2026-10-25** on `release/v0.23.0`; native release acceptance remains pending.

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
| [APP-05](#app-05) | Medium | Integration | External connector and connection lifecycle acceptance remains unverified | From code and available environment |
| [QA-03](#qa-03) | Medium | Release acceptance | Native accessibility, desktop workflow and macOS/Linux evidence remains pending | Not executed |
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
- **Fix direction:** FG-02, planned for v0.30: check on startup (off / notify / auto-install), then download and apply. Depends on REL-01 and signing (REL-02).

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

---

<a id="app-05"></a>
### APP-05 — External connector acceptance

Toolbar contributions now render and follow registry state. Core connection activation exists, but no desktop/CLI connection-management caller was found. The separate external connector repository is unavailable here, so its package-only desktop/CLI acceptance remains open. Keep the v0.23 integration gate pending; see [v0.24 acceptance](specs/editor-comfort/acceptance.md).

## Quality

<a id="qa-03"></a>
### QA-03 — Native and supported-platform acceptance

Windows headless tests cover a 100-target keyboard session, paging, save/reopen, light/dark layouts, narrow windows, RTL and generated restrictions. Native desktop input, screen-reader announcements, operating-system text scaling, Linux/macOS execution and compatibility M7c acceptance were not executed in this environment. They remain release gates rather than inferred passes.


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
