---
title: "Known Issues & Unfinished Features"
status: active
updated: 2026-11-26
summary: "Open issues in migration from earlier versions, release distribution, Source Control, external connectors and platform acceptance."
---
# Known Issues & Unfinished Features

This file lists what is wrong *now*. Fixed bugs are not kept here: they are in [CHANGELOG.md](../CHANGELOG.md) (see [Where the old fixes went](#where-the-old-fixes-went)). When a bug is fixed, delete its row and section here and add a `### Fixed` line to the changelog `[Unreleased]` section. Planned work for an ID is linked from [the roadmap](todos/future-roadmap.md) (REL, APP, QA); the roadmap does not describe it again. The WPF app is gone from `main` (source: branch `legacy/wpf`), so nothing here is about WPF.

**How each issue was checked.** "Reproduced" means the case was run through the real load and save strategies and the output is quoted. "From code" means the cause was identified in the source without running the case. Run the same cases again before fixing, and turn each repro into a test in `tests/Toucan.Core.Tests/Formats/`.

## Open issues

| ID | Severity | Area | Problem | Checked |
|----|----------|------|---------|---------|
| [MIG-02](#mig-02) | Low | Migration | A project from an earlier version opens with unsaved changes, so closing it asks to save | Reproduced |
| [REL-01](#rel-01) | Medium | Release | Tag-triggered release workflow is written but has not run yet | From workflow |
| [REL-02](#rel-02) | Medium | Release | macOS app is ad-hoc signed and not notarized | From docs and script |
| [REL-04](#rel-04) | Low | Release | Windows installer is built by the release workflow, which has not run yet; it is unsigned for 1.0 | From workflow |
| [APP-01](#app-01) | Low | App | Source Control panel does not exist | Reproduced |
| [APP-05](#app-05) | Medium | Integration | External connector and connection lifecycle acceptance remains unverified | From code and available environment |
| [QA-03](#qa-03) | Medium | Release acceptance | Native Windows accessibility checked on another machine; evidence not in repo. macOS/Linux assumed passed | Recorded by maintainer |
| [QA-04](#qa-04) | Low | Release acceptance | AI providers tested against stubbed HTTP only; no live-service run recorded | From tests |
| [QA-02](#qa-02) | Info | Perf | Large-project performance has never been profiled | Not checked |

Severity: **High** loses or corrupts user data; **Medium** gives wrong results or blocks a release goal; **Low** is a gap or a cosmetic problem; **Info** is a known unknown.

Suggested order: release work first (REL-01), then the rest.

---

## Format bugs

None are tracked. What each format keeps and drops on save is in [formats.md](formats.md); the tests that guard it are `FormatFidelityFixtureTests`, `FormatStabilityTests`, `FormatBugRegressionTests` and `YamlFidelityTests` in `tests/Toucan.Core.Tests/Formats/`.

---

## Migration

<a id="mig-02"></a>
### MIG-02: Earlier projects open with unsaved changes

- **Severity:** Low · **Checked:** reproduced with the 0.23.0 JSON fixture.
- **State:** opening a project from an earlier version upgrades it in memory (exact-resource mapping, comment sidecars) and marks it changed, so closing asks whether to save although nothing was edited. Opening itself writes nothing.
- **Fix direction:** keep migration-only changes out of the unsaved state, or say in the prompt that the project format was updated.

---

## Release and distribution

<a id="rel-01"></a>
### REL-01: Tag-triggered release not run yet

- **Severity:** Medium · **Checked:** `.github/workflows/release.yml` (2026-11-26).
- **State:** pushing a tag `v<version>` checks the tag against `Directory.Build.props` and the evidence records, runs the CI test matrix, builds the Windows ZIP and installer, Linux `.tar.gz`, `.deb` and `.rpm` (x64 and arm64) and macOS DMGs (arm64 and x64), runs `--self-check` on each (the Windows installer and the `.deb` are installed, checked and removed), and attaches everything with `SHA256SUMS.txt` to a draft release. It has not run on GitHub yet, and `packaging/build-linux-packages.sh` has not run anywhere.
- **Fix direction:** push a release-candidate tag (`v1.0.0-rc.1`) once the test records exist, and fix what the first run finds.

<a id="rel-02"></a>
### REL-02: macOS app is not notarized

- **Severity:** Medium · **Checked:** README and `packaging/build-macos-app.sh`.
- **State:** the build is ad-hoc signed. The script already signs with a Developer ID and notarizes when `SIGN_IDENTITY` and `NOTARY_PROFILE` are set, but no such identity is configured.
- **Impact:** Gatekeeper blocks the first launch ("could not verify"). Users must use Open Anyway or clear the quarantine flag. This is documented in the README but costs installs.
- **Fix direction:** the release workflow signs and notarizes when the secrets in [release signing](release-signing.md) exist, and refuses a final release without them. Waiting on the maintainer's Apple Developer account.

<a id="rel-04"></a>
### REL-04: Windows installer not built by CI yet

- **Severity:** Low · **Checked:** release workflow and `packaging/installer.iss`.
- **State:** the release workflow builds the Inno Setup installer next to the portable ZIP, installs it silently, runs `--self-check` and uninstalls it. Toucan 1.0 ships unsigned on Windows (decided 2026-11-26), so SmartScreen asks once; [install.md](install.md) shows the steps. MSIX is not part of releases.
- **Fix direction:** confirm on the first release-candidate run; record `install-upgrade@Windows` by hand.

---

## App gaps

<a id="app-01"></a>
### APP-01: Source Control panel does not exist

The Dictionary panel is implemented. A Source Control panel and Git workflow remain planned after v1.0. See the [roadmap](todos/future-roadmap.md#git-integration-fg-06).

---

<a id="app-05"></a>
### APP-05: External connector acceptance

**Release decision (v0.25):** not a blocker. No v0.25 workflow depends on an external connector, and the release notes make no connector or online-sync claim.

Toolbar contributions now render and follow registry state. Core connection activation exists, but no desktop/CLI connection-management caller was found. Package-only desktop/CLI acceptance still requires testing with the external connector. See the [acceptance record](specs/editor-comfort/acceptance.md).

## Quality

<a id="qa-03"></a>
### QA-03: Native and supported-platform acceptance

Windows headless tests cover a 100-target keyboard session, paging, save/reopen, light/dark layouts, narrow windows, RTL and generated restrictions. Native Windows input, screen-reader announcements and text scaling were checked on a separate Windows machine; that evidence is not stored in this repository, and the compatibility M7c rows should be updated from it. The 2026-10-25 readiness review records macOS/Linux testing as assumed passed, without execution artifacts. See the [acceptance record](specs/editor-comfort/acceptance.md). Since 2026-11-26 results become committed records: run `tools/record-evidence.sh tests` (or `pwsh tools/record-evidence.ps1 tests`) and the hand checks on each machine, and `tools/check-evidence.py` shows what is missing (see the [release checklist](release-checklist.md)). macOS has a passing test record; Windows and Linux records and all hand checks are still to be recorded.


<a id="qa-04"></a>
### QA-04: AI not tested against live services

- **Severity:** Low · **Checked:** `AiIntegrationTests` and `DeepLProviderTests` use a stub `HttpMessageHandler` and clear `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` and `GEMINI_API_KEY`. No run against a real provider is recorded for v0.25.
- **Impact:** a provider API change, such as a renamed model or a new response shape, would show up only for users. No data is at risk: AI is optional, every result opens in a preview, is applied only when accepted, and can be undone.
- **Release decision (v0.25):** not a blocker. The release notes label AI features preview.
- **Fix direction:** an opt-in test run with real keys from CI secrets, recording one translation, one analysis and one re-translation per provider.

<a id="qa-02"></a>
### QA-02: Performance not profiled

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
| QA-01 | Missing regression tests for B1 to B6 and the format edge cases | v0.24.0 (`Qa01RegressionTests`, `FormatFidelityFixtureTests`, `FormatStabilityTests`) |
| FMT-05 to FMT-09 | RESX language detection, XLIFF save losing source/notes/state, ARB metadata and region locales, YAML flat keys and `__self` | v0.24.0 |
| v0.14.1 to v0.16.1 | Earlier bug batches | 0.14.1, 0.14.2, 0.15.0, 0.16.1 |

B7 to B11 were fixed in the WPF app. Whether the Avalonia app has the same problems was not checked in this audit.
