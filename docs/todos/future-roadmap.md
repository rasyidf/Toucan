---
title: "Toucan roadmap: v0.21 to v1.0"
status: in-progress
progress: "33 v1 checklist tasks; 8 v1 gates; 30 deferred tasks"
updated: 2026-11-26
summary: "v0.26 to v0.29 shipped in the v0.26.0 pre-release. Feature work is frozen; the v1 checklist maps each v1.0 gate to correctness, contract, evidence, distribution, performance and release tasks."
---
# Toucan roadmap: v0.21 to v1.0

## Product goal

Open a localization project, understand what needs work, translate with context, review confidently, and save or synchronize without losing information. Local files remain a first-class workflow; online connections are optional.

The order is **reliability → daily workflow → content sources → distribution → stabilization**. Each release has one main outcome and a completion gate. Versions describe the intended sequence, not dates or a promise to ship unfinished work. Split a milestone further if its scope grows; do not move unresolved safety work into v1.0 merely to keep the version schedule.

## Maintaining this roadmap

This file lists open work only. Remove shipped tasks instead of ticking them, except in the v1 checklist, where closed items are ticked until 1.0 ships so each gate shows its progress. Add user-visible changes to `CHANGELOG.md` under `[Unreleased]`, regenerate the [website changelog](../changelog.html) with `python3 tools/generate-changelog.py`, lasting capabilities to [completed-features.md](../completed-features.md), and remove resolved issues from [known-bugs.md](../known-bugs.md). Keep the README roadmap and [website summary](../index.html) aligned, then refresh `docs/INDEX.md`.

Existing bugs are linked by ID instead of duplicated here. Completion gates describe acceptance criteria; checkboxes describe implementation work. The header counts release tasks, v1.0 gates, and deferred tasks separately.

## Release overview

The [2026-10-25 source audit](../research/v0.24-roadmap-audit.md) distinguishes implemented UI and compatibility work from unfinished product workflows. v0.24 already implements its editor/onboarding features; its remaining task is release acceptance. The main plugin toolbar and macOS native menu are implemented. Later milestones retain the work that their existing foundations do not yet deliver.

| Release | Main outcome | Depends on |
|---|---|---|
| v0.21 | File fidelity and automated regression checks | v0.20 |
| v0.22 | Recoverable saves and durable drafts | v0.21 |
| v0.23 | Extension platform: lifecycle, commands, desktop contributions, host services | v0.22 |
| v0.24 | Comfortable editing and onboarding | v0.23 |
| v0.25 | Glossary and message-aware validation | v0.24 |
| v0.26 | Review state that follows source changes (shipped 0.26.0) | v0.25 |
| v0.27 | Content-source contracts and local snapshots | v0.23, v0.26 |
| v0.28 | CRUD persistence and synchronization engine | v0.27 |
| v0.29 | One complete online-source integration | v0.28 |
| v0.30 | Repeatable distribution and update notifications | CI from v0.21 |
| v0.31 | Measured performance, migration checks, release candidate | v0.21–v0.30 |
| v1.0 | First stable release after all release gates pass | v0.31 |

### v0.21: File fidelity and CI

**Outcome:** supported formats can be edited without losing information, with automated protection against regressions.

**Status:** complete. The cross-platform CI workflow is fixed and finalized; no CI work remains open. Coverage stays a report, not a release criterion.

**Completion gate:** every format advertised as editable passes its documented fidelity fixtures, and CI catches the known regression cases.

### v0.22: Safe saves and recovery

**Outcome:** users can preserve incomplete work and recover from interrupted persistence.

**Completion gate:** failure scenarios preserve a recoverable copy and never report unsaved work as saved.

### v0.23: Extension platform

Shipped in v0.23.0. The extension platform (registration/activation split, command registry, desktop contribution package, host services, workspace API, compatibility checks, SDK harness and sample connector) is in [CHANGELOG.md](../../CHANGELOG.md) and [completed-features.md](../completed-features.md). The sample connector built outside this repository loads on desktop and in the CLI, its command is in the palette and a menu with a reassignable shortcut and becomes unavailable when its workspace closes, and a plugin batch edit behaves like a native edit through undo, save failure, recovery and reopen.

The upstream release records its connector acceptance as passed. Revalidation of the merged source build and the production connection-management gap remain tracked in APP-05 and QA-03.

### v0.24: Editor comfort and onboarding

**Outcome:** a new user can open a project and complete a keyboard-driven translation session comfortably. **References:** QA-03. Implementation and Windows automated evidence are in [editor-comfort acceptance](../specs/editor-comfort/acceptance.md).


**Completion gate:** a user can work through 100 strings without losing their place or needing a mouse for routine actions; accessibility and layout issues found in that flow are resolved.


### v0.25: Terminology and validation

**Outcome:** translations follow project vocabulary and preserve executable message syntax.

**Status:** shipped as app version 0.25.0. Local verification is archived in [v0.25 readiness](../specs/v0.25-readiness.md); native Windows accessibility checks were run on a separate machine. Supported behavior and limits are in [terminology and validation](../specs/v0.25-terminology-validation.md). The ARB comment decision moved to V1-10.

**Completion gate:** glossary and message fixtures detect meaningful errors without blocking draft saves; accepting a suggestion follows normal undo and dirty tracking.

### v0.26 to v0.29: Review correctness and content sources

Shipped together in the v0.26.0 pre-release (2026-11-25): review state, Needs review filters, reject and approve with comment, review history, the strict-validation push hold-back for key/value services, plugin API 1.3, accounts, the local-folder source, the sync engine, the Sources panel, `toucan sources` commands, the Crowdin connector (preview) and Open from a service. Details are in [CHANGELOG.md](../../CHANGELOG.md) and [content-sources tasks](../specs/content-sources/tasks.md).

Open items moved to the [v1 checklist](#v1-checklist): live Crowdin writes and failure cases (V1-15, V1-16), the Sources accessibility pass (V1-52) and packaged keyring checks (V1-45, V1-60). Staged plugin installation, publisher signatures and entitlement checks from the v0.29 plan did not ship and move to 1.x with the plugin feed.

### v0.30: Distribution and updates

**Outcome:** releases are repeatable, installable, and discoverable. **References:** REL-01, REL-02, REL-03, REL-04.

Tasks are in the [v1 checklist](#v1-checklist).

**Completion gate:** a release can be built from its tag and installed/upgraded on each supported platform, with explicit platform limitations and no manual artifact patching. Automatic in-app installation is deferred.

### v0.31: Performance and release candidate

**Outcome:** the complete workflow has measured limits and survives upgrade and failure scenarios. **Reference:** QA-02.

**Proposed performance budgets:** usable editor within five seconds for the reference fixture, warm search within 200 ms, and responsive typing while persistence runs. Confirm or revise these budgets against named hardware before claiming them; they are not current measurements.

Tasks are in the [v1 checklist](#v1-checklist).

**Completion gate:** measured budgets are met or the supported scale is explicitly narrowed, migrations pass, and no unresolved data-loss or silent-overwrite issue remains.

## v1.0 release gates

Version 1.0 ships only when every gate below is checked. Feature work is frozen: from here only correctness, contracts, distribution, verification and release work lands; everything else is 1.x (see [Future plan](#future-plan-after-v10)). The checklist below owns the remaining v0.30 and v0.31 tasks; those sections keep only their outcome and completion gate.

- [x] **G1 Formats.** Every format advertised as editable preserves its documented semantics and metadata; unsupported constructs have safe, visible handling. Tasks: V1-10 to V1-12.
- [ ] **G2 Recovery.** Failed saves, crashes and interrupted synchronization preserve recoverable work and accurate pending state. Tasks: V1-13 to V1-15.
- [ ] **G3 Connectors.** Stable online connectors handle concurrency, permissions, partial success and retry/recovery; preview connectors are clearly identified. Tasks: V1-15, V1-16.
- [ ] **G4 Contracts.** Plugin API, project, account, sync-state and source-protocol contracts are frozen, with a written migration and downgrade policy and migration tests from every released preview. Tasks: V1-20 to V1-27.
- [ ] **G5 Evidence.** Daily editing and review workflows pass automated and packaged-build checks on Windows, macOS and Linux, each backed by a machine-readable record for the release commit. Tasks: V1-30 to V1-32, V1-60.
- [ ] **G6 Distribution.** Installation, update discovery, upgrade, uninstall and settings preservation are tested and documented per OS. Tasks: V1-40 to V1-46.
- [ ] **G7 Performance and accessibility.** Both have recorded verification; budgets are met or the supported scale is narrowed and published. Tasks: V1-50 to V1-52.
- [ ] **G8 Release.** A release candidate is published, known limitations are published, and no release-blocking issue remains. Tasks: V1-60 to V1-62.

### v1 checklist

Items marked **(you)** need the maintainer: a product decision, an account, money, a write to a real external service, or a run on a machine other than the macOS dev machine. Work stops at those items and asks.

#### 0. Housekeeping found while planning

- [x] V1-00 `v0.26.0` is tagged and published as a pre-release, but `Directory.Build.props` still says `0.25.0` and `CHANGELOG.md` still has everything under `[Unreleased]`. Cut a `[0.26.0] - 2026-11-25` section, bump the version for the next build, and have the tag workflow (V1-40) refuse a tag that does not match the version. The published v0.26.0 has no package assets. Done: changelog cut as 0.26.0 and version set to 0.26.0 so builds from `main` stop offering 0.26.0 as an update; the tag check stays with V1-40.
- [x] V1-01 Remove the tracked `.DS_Store` and `samples/.DS_Store` (already in `.gitignore`).

#### 1. Correctness (G1, G2, G3)

- [x] V1-10 **(you)** Decide ARB comment handling: keep `@key.description` in the `.arb` file, or move comments to `.toucan` sidecars under document saving. Blocks V1-11 for ARB. Decided 2026-11-26: comments stay in `@key.description` (Flutter's gen-l10n turns them into doc comments). This is already how the folder and exact-resource ARB writers behave.
- [x] V1-11 For each of the 14 formats in [formats.md](../formats.md), either migrate it to document saving with fidelity fixtures (extend the verified list in `OnlyVerifiedBuiltInFormatsDeclareTargetedDocumentSaves`), or label it **Basic** or **Full rewrite** in the UI and in formats.md. Today only `json` and `namespaced` use document saving. Bring the per-format proposal before migrating. Finding (2026-11-26): there are three save paths, and document saving is the least used. Opening a folder auto-assigns a framework only when its format supports exact-resource discovery (the JSON script frameworks), and those projects save through the layout-keeping exact-resource writers. Every other folder saves through the format's full-rewrite writer. Document saving applies only when `toucan.tproj` names a document-capable framework (i18next, generic JSON). ARB document saving with comments in the file is parked on branch `wip/arb-document-saving`: it also needs `@key` placeholders carried through document units, or the placeholder editor loses them, and a detection change to reach plain folders. Decided 2026-11-26: finish ARB first. Done: ARB folders (and older ARB projects without a framework) open through document saving; comments stay in `@key.description`, placeholders and unknown `@key` fields travel with each message, and only edited tokens change (`ArbDocumentTests`, `ArbDocumentProjectTests`, including approvals kept from full-rewrite projects). Java `.properties` done the same way: a Java (ResourceBundle) framework profile, line-level saving that keeps comments, separators, continuations and escapes, ISO-8859-1 kept (`PropertiesDocumentTests`); older projects move only when their language names match the bundle's, so approvals are never orphaned. YAML stays on the full rewrite for 1.0 (decided 2026-11-26; document saving for it is in the 1.x backlog). Labels done: Project Properties shows how the open project saves (*Edits in place*, *Rewrites the file*, *Export only*, *Read only*) and `formats.md` has a Saving column, both from `FormatSaving` (`FormatSavingTests`). Plain i18next folders now also open through document saving.
- [x] V1-12 Fix or label the namespaced JSON save that writes both `<lang>.json` and `locales/<lang>/<ns>.json` so a reload sees each key twice (listed as a limitation in formats.md; it breaks G1 for an editable format). Done: namespaces are written only under `locales/<lang>/`, top-level values stay in `<lang>.json`; the pinned quirk test is replaced by `NamespacedSaveWritesEachKeyOnceSoAReloadSeesItOnce`.
- [x] V1-13 Extend the strict-validation hold-back (`requireValidForApproval`) to whole-file connectors. Done: `SyncDeliveryPolicy.HoldBackInvalidDocuments` holds every upload of a file whose saved values fail validation (all uploads when a blocked value has no known file) and adds a notice per file; deletions still go out. Covered by `DocumentHoldBackTests` and `SyncDeliveryPolicyTests`.
- [ ] V1-14 Find and fix the intermittent `EditorLayoutTests` failure at width 1280, dark theme. Record the cause, not a retry. Not reproduced on the macOS dev machine (2026-11-25): 25 isolated runs, 10 runs under load, 6 full desktop-suite runs and 3 whole-solution runs all passed. The test's assertions now report window size, theme, card width and classes and each box's width, so the next failure records its cause. Fixed on the way: `CommandRegistryTests.ProgressAndCompletionAreRaised`, which did fail under load (late progress after completion). **(you)** Which machine showed the failure, and is its output still available?
- [ ] V1-15 Crowdin interrupted and partial sync: first add faults to `FakeCrowdin` (crash mid-push, timeout after commit, 403 permission error, partial success across files) and make the connector pass; then **(you)** approve and supply a throwaway Crowdin project for the same cases live. Live writes have never run. Stand-in part done (`CrowdinWorkflowTests`, interrupted and partial pushes): a failed file is retried alone, a 403 leaves the file pending, a timeout after import is confirmed by reading back, and a crash after an upload converges on restart without sending it again. Limit found: if Crowdin reformats an upload and Toucan stops before recording it, the next run shows a conflict for that file (no overwrite, no resend). Live run pending.
- [ ] V1-16 **(you)** Decide whether Crowdin ships stable or as clearly labelled preview, using V1-15 results. Covers APP-05.

#### 2. Freeze contracts (G4)

- [x] V1-20 Review plugin API 1.3 (`Toucan.Plugins.Abstractions`, including `Sources/`) and add a public-API baseline test so any change fails CI. Done: baselines for the three plugin packages in `docs/api/` (`PublicApiTests`). Review notes: 238 public types in Abstractions; 98 of them sit in `Toucan.Core.*` namespaces for historical reasons and are frozen under those names; nothing is obsolete. **(you)** confirm the content-source types (`Toucan.Plugins.Sources`, 94 types, one preview connector so far) are frozen with the rest rather than kept as preview in 1.x.
- [x] V1-21 Review and freeze the `toucan.tproj` schema (`docs/toucan.project.schema.json`) with a schema version field. Done: `manifestVersion` (absent = 1); a newer version is refused; a damaged manifest is reported instead of being treated as missing and overwritten (it was); the app manifest's schema is generated as `docs/toucan.tproj.schema.json`. The older `toucan.project.schema.json` describes the BabelEdit-shaped manifest and stays.
- [x] V1-22 Review and freeze `accounts.json`. Done: a newer `version` is left untouched and read-only (it used to be emptied on the next change); a damaged file is set aside. `settings.json` got the same protection and keeps unknown settings.
- [x] V1-23 Review and freeze the sync state files (`baseline.json`, `journal.json`) and their schema version. Already safe: a newer or damaged file is set aside and the next sync is planned as a first sync. They use PascalCase keys unlike the other files; frozen as they are.
- [x] V1-24 Review and freeze the Toucan source protocol (`source-protocol.md`, OpenAPI) as 1.0. Done: `/v1` marked frozen; its additive-only rule already covered compatibility.
- [x] V1-25 Write `docs/compatibility.md`: what is frozen, how each file migrates forward, what an older build does with a newer file (set aside, never overwritten), backup before migration, and the supported downgrade path.
- [x] V1-26 Migration tests from fixtures: projects and settings written by each released preview (0.23.0, 0.24.0, 0.25.0, 0.26.0). The v0.27–v0.29 milestones shipped inside the 0.26.0 tag, so 0.26.0 fixtures cover sources, accounts and sync state.
- [x] V1-27 Review state, review history and recovery files included in V1-26. Done: fixtures written by the real 0.23.0–0.26.0 code (`tools/release-fixtures/`) for JSON, i18next, ARB, `.properties`, YAML, PO and user settings; `ReleaseMigrationTests` opens each through the lifecycle service and compares with what that release read. Found and fixed: approvals and comments from 0.24 i18next projects were lost; 0.23 ARB sidecar comments were lost; my new detection mis-read 0.23's root-level `en.properties` and switched projects with two files per language. MIG-01 fixed (2026-11-26): 0.23 YAML and `.properties` projects with duplicated files open merged, the copy's value winning where they differ, and the next save moves the copies to a backup (`LegacyCopyMergeTests`, `ReleaseMigrationTests`). MIG-02 fixed (2026-11-27): comments kept beside the files were read after the store recorded what was saved, so every project with such comments opened with unsaved changes, older or not; they are now read first.

#### 3. Portable verification evidence (G5)

- [x] V1-30 CI matrix: `ci.yml` `all` mode on Windows, macOS and Linux uploads TRX and JUnit per OS and writes a pass/fail summary per OS to the job summary. Runs on the release tag (V1-40). Done: each OS in `ci.yml` writes JUnit XML and a results table to the job summary (`tools/trx_report.py`), writes an evidence record with the same scripts people run, and uploads TRX, JUnit and the record as `test-results-<os>` (30 days). The tag trigger arrives with V1-40.
- [x] V1-31 `tools/record-evidence.sh` and `tools/record-evidence.ps1`: write `docs/evidence/<version>/<os>-<check>-<date>.json` with commit, OS and version, architecture, command or check name, pass/fail/skip counts, date and who ran it, plus a JSON schema; commit the record. Used for hand checks (screen readers, Gatekeeper, installers, keyring). Existing v0.24/v0.25 JSONs stay as history. Done: `docs/evidence/schema.json` and README; records go to `docs/evidence/<version>/<os>-<check>-<time>.json`. The PowerShell script was first run by hand on Windows 11 on 2026-11-27; it wrote times in the machine's culture (`01.17.42`), now fixed to invariant culture, and the check ignores records with a malformed time.
- [x] V1-32 Release checklist (`docs/release-checklist.md`) and a `tools/check-evidence` step: a release needs a passing record per OS for the release commit. WSL counts for neither packaged builds nor the keyring. **(you)** run the Windows and Linux records on those machines. Closes QA-03. Done: [release checklist](../release-checklist.md) and `tools/check-evidence.py` (latest record per requirement decides; records with uncommitted changes do not count; WSL is refused for packaged and keyring checks). Test records for all three OSes exist since 2026-11-27 (Windows 11 native, Ubuntu 24.04 under WSL, macOS), and `check-evidence.py --only-tests` passes. Records also survive commits that touch only Markdown or the evidence tools.

#### 4. Distribution (G6; REL-01 to REL-04)

- [ ] V1-40 Tag-triggered release workflow: version check against the tag, build all packages, `SHA256SUMS`, packaged smoke test per OS, draft release with assets. Closes REL-01. Written: `.github/workflows/release.yml` (tag = version, evidence check, CI matrix, packages, `--self-check` on each, Windows installer and `.deb` installed and removed, `tools/release_assets.py` checks the set and writes checksums, draft release). Not run yet: needs a release-candidate tag.
- [ ] V1-41 macOS Developer ID signing and notarization in CI from secrets (`build-macos-app.sh` already supports `SIGN_IDENTITY` and `NOTARY_PROFILE`). **(you)** Apple Developer Program membership, a Developer ID Application certificate (.p12 and password), team ID, and a notary credential (App Store Connect API key). Keep the ad-hoc development path. Closes REL-02. Prepared: the release workflow imports a Developer ID certificate into a throwaway keychain, notarizes with an App Store Connect API key, checks `spctl` and `stapler`, and refuses a final release without the secrets. What to supply: [release signing](../release-signing.md).
- [ ] V1-42 Windows installer from `packaging/installer.iss`, built in the release workflow next to the portable zip. **(you)** decide whether to buy Windows code signing for 1.0 or ship unsigned with the SmartScreen warning documented. Closes REL-04. Decided 2026-11-26: unsigned for 1.0. Built, installed, self-checked and uninstalled in the release workflow (not run yet).
- [x] V1-43 **(you)** choose the Linux package (proposal: `.tar.gz` plus `.deb` for x64 and arm64). Document dependencies, desktop entry, install and removal, and the Secret Service requirement (GNOME Keyring or KWallet; without it the key stays in a file). Decided 2026-11-26: `.tar.gz`, `.deb` and `.rpm` for x64 and arm64. Done: `packaging/build-linux-packages.sh` (desktop entry, icon, `.tproj` MIME type, `toucan` command, libsecret recommended) and [install.md](../install.md) with the Secret Service requirement; first run happens in the release workflow.
- [x] V1-44 Notify-only update check on startup (Off / Notify, default Notify), using the existing `UpdateService` and channel; shows the version and opens the release page. No download or install. Closes REL-03. Done: at most daily, after the window opens; a notification with a Download action; Settings → About switch. Version comparison now orders pre-release labels, so a release candidate learns about its final release (`AppearanceAndUpdateTests`).
- [ ] V1-45 Install, upgrade from 0.26.0, uninstall and settings/accounts/secrets survival tested per OS on packaged builds, recorded with V1-31.
- [x] V1-46 Update README, packaging/README and the site for the new packages. Done: [install.md](../install.md), [release signing](../release-signing.md), packaging README.

#### 5. Performance and accessibility (G7; QA-02)

- [x] V1-50 Profile the 10,000 keys × 10 locales fixture in a Release build on named hardware: cold and warm open, search, edit, save, pull/plan/push, memory. Record with V1-31. Sync is partly measured in `SyncScaleTests` (Debug, memory not measured). Done: `PerformanceProfileTests` (opt-in, Release) and [performance](../performance.md) with 1,000 to 10,000 keys on a MacBook Air M1. Found and fixed: per-row validation against the whole project made opening quadratic (about an hour at 10,000 keys, now 10.6 s).
- [x] V1-51 Meet the proposed budgets (usable in 5 s, warm search under 200 ms, responsive typing during save/sync) or **(you)** approve a narrower supported scale; publish it. Closes QA-02. Measured: budgets hold up to about 2,000 keys × 10 languages for opening (3.7 s), not for text search (451 ms); at 10,000 keys open is 10.6 s, search about 0.8 s, edits 2.2 s and saving all files runs out of memory. Decided 2026-11-26: narrow the scale. Toucan 1.0 supports 20,000 translations (2,000 keys × 10), with budgets of 5 s to open, 250 ms key search, 500 ms text search and 100 ms UI stalls while saving, all met; larger projects open with a notice (`SupportedScaleTests`). Closes QA-02 for 1.0.
- [ ] V1-52 Content-sources task 5.8: keyboard, screen reader and visual review of the Sources panel, accounts, add-connection, plan, conflict and open-from-service screens. VoiceOver on macOS here; **(you)** Narrator/NVDA on Windows and Orca on Linux, recorded with V1-31. Automated part done: `EverySourcesScreenIsNamedAndReachableByKeyboard` checks every control on the Sources panel, the six wizard pages, Accounts and the three plan tabs; it found unnamed switches, now named by their settings row for the whole app. Screen-reader runs still to do.

#### 6. Release candidate (G8)

- [ ] V1-60 Packaged full flow on each OS (open, edit, validate, review, save/sync, close, reopen), recorded per OS for the RC commit.
- [x] V1-61 Known-limitations page (`docs/known-limitations.md`): preview features (AI, preview connectors), unsupported format constructs, platform limits, supported scale. Done: [known limitations](../known-limitations.md), linked from the README and the documentation page. It lists Crowdin as a preview until V1-15's live run and V1-16 decide otherwise; update it then.
- [ ] V1-62 **(you)** approve publishing the RC; resolve release-blocking feedback; close G1 to G8 here and in known-bugs.md and CHANGELOG.md.

Not v1: QA-04 (AI stays labelled preview and is listed in V1-61), APP-01, and everything under [Future plan](#future-plan-after-v10).

## Feature-group cross-reference

Existing docs refer to FG IDs. These map the former groups to the smaller release plan; release sections above own the tasks.

| Group | Planned location |
|---|---|
| FG-01 Packaging & Distribution | v0.30 |
| FG-02 Auto-Updater | Update notifications in v0.30; automatic installation after v1.0 |
| FG-04 ConsistencyAI | v0.25 |
| FG-05 Review Workflow | v0.26 |
| FG-06 Git Integration | After v1.0 |
| FG-09 Improved Validation | v0.25 |
| FG-10 Onboarding & UX Polish | v0.24 |
| FG-11 Editor Improvements | v0.24; Markdown preview after v1.0 |
| FG-12 Export & Reporting | After v1.0 |
| FG-13 Performance & Scale | v0.31 |
| FG-14 Webhook Provider | After v1.0; machine translation remains separate from content-source CRUD |
| FG-15 Glossary & Terminology | v0.25; TBX after v1.0 |
| Extension platform (new) | v0.23; source contracts v0.27; entitlements and signing v0.29 |
| FG-16 Test Coverage & CI | v0.21 and focused workflow/failure checks in subsequent releases |
| Content sources (new) | Snapshot mode v0.27, CRUD/sync v0.28, first online connector v0.29 |

## Future plan: after v1.0

These remain visible backlog items, not prerequisites for the first stable release.

### Additional sources and synchronization

- [ ] Add remaining Crowdin, Lokalise, Phrase, and Transifex adapters after the first connector proves the contracts.
- [ ] Add optional scheduled refresh, delta reads, and automatic background synchronization with the same conflict and recovery guarantees.
- [ ] Add declarative custom REST source mappings if recurring integration needs justify them.

### Git integration (FG-06)

- [ ] Detect Git repositories and show per-key changes since the last commit.
- [ ] Add a Source Control panel with staged/unstaged files and per-key diffs (APP-01).
- [ ] Add optional auto-commit on save with a configurable message template.

### Updates and platform integration

- [ ] Add verified update download/application with restart, recovery, and stable/preview channels.
- [ ] Add Linux AppImage/Flatpak distribution and complete XDG integration.
- [ ] Add macOS system-accent integration; the native menu bar and native shortcut ownership already exist.

### Editor, glossary, and reporting

- [ ] Add Markdown preview for translation values.
- [ ] Add a syntax-highlighted value editor.
- [ ] Raise the supported scale to 100,000 translations: editor cards only for the shown page, incremental validation of edits, and a desktop save that does not run out of memory (QA-02, [performance](../performance.md)).
- [ ] Save YAML through document saving: a line-level editor that keeps comments, and a key root for Rails locale files (`en:`), with migration from full-rewrite projects.
- [ ] Add glossary TBX import/export.
- [ ] Export translation-progress reports as HTML/PDF and missing/unused-key reports per language.
- [ ] Export changes since a selected date and add a statistics dashboard with trends.
- [ ] Track exports as delivery: remember which export last included each translation's current value and show it beside the delivery state.

### Custom webhook translation engine (FG-14)

- [ ] Add configurable request-body templates and response-path mapping.
- [ ] Add timeout settings and safe retries for translation requests.
- [ ] Add opt-in on-save webhook notifications with explicit destination and payload configuration.

### Plugin system

Core plugins shipped in v0.18.0. See the [plugin guide](../plugins.md) and [original plan](../archive/plugin-system-plan.md).

- [ ] Add per-project validation rule files under `.toucan/rules/`.
- [ ] Make signing mandatory for all plugins and add a plugin feed alongside the collaboration/auth milestone (commercial-channel verification ships in v0.29).

### Real-time collaboration

- [ ] Build Toucan Hub for authenticated presence and locking.
- [ ] Add WebSocket edit synchronization and concurrent-session conflict handling using the source-sync foundations.

### Advanced AI

- [ ] Add embedding-based translation-memory similarity.
- [ ] Add brand voice/tone profiles and translation-quality scoring.
- [ ] Extend deterministic glossary validation with optional domain-aware AI checks.

### CI/CD integrations

- [ ] Publish a GitHub Action for `toucan check`.
- [ ] Publish a GitLab CI template.
- [ ] Add configurable missing-translation thresholds for build failures.
