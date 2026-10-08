---
title: "Toucan roadmap: v0.21 to v1.0"
status: in-progress
progress: "58 release tasks; 6 stable gates; 27 deferred tasks"
updated: 2026-10-09
summary: "Eleven focused releases from v0.21 to v0.31: file safety, extension platform, editing, terminology, review, snapshot and CRUD sources, one online connector, distribution, and measured stabilization."
---
# Toucan roadmap: v0.21 to v1.0

> Current release: **v0.21.0** on Windows, macOS and Linux. Every build before v1.0 is a preview.

## Product goal

Open a localization project, understand what needs work, translate with context, review confidently, and save or synchronize without losing information. Local files remain a first-class workflow; online connections are optional.

The order is **reliability → daily workflow → content sources → distribution → stabilization**. Each release has one main outcome and a completion gate. Versions describe the intended sequence, not dates or a promise to ship unfinished work. Split a milestone further if its scope grows; do not move unresolved safety work into v1.0 merely to keep the version schedule.

## Maintaining this roadmap

This file lists open work only. Remove shipped tasks instead of ticking them. Add user-visible changes to `CHANGELOG.md` under `[Unreleased]`, regenerate the [website changelog](../changelog.html) with `python3 tools/generate-changelog.py`, lasting capabilities to [completed-features.md](../completed-features.md), and remove resolved issues from [known-bugs.md](../known-bugs.md). Keep the README roadmap and [website summary](../index.html) aligned, then refresh `docs/INDEX.md`.

Existing bugs are linked by ID instead of duplicated here. Completion gates describe acceptance criteria; checkboxes describe implementation work. The header counts release tasks, v1.0 gates, and deferred tasks separately.

## Release overview

| Release | Main outcome | Depends on |
|---|---|---|
| v0.21 | File fidelity and automated regression checks | v0.20 |
| v0.22 | Recoverable saves and durable drafts | v0.21 |
| v0.23 | Extension platform: lifecycle, commands, desktop contributions, host services | v0.22 |
| v0.24 | Comfortable editing and onboarding | v0.23 |
| v0.25 | Glossary and message-aware validation | v0.24 |
| v0.26 | Review state that follows source changes | v0.25 |
| v0.27 | Content-source contracts and local snapshots | v0.23, v0.26 |
| v0.28 | CRUD persistence and synchronization engine | v0.27 |
| v0.29 | One complete online-source integration | v0.28 |
| v0.30 | Repeatable distribution and update notifications | CI from v0.21 |
| v0.31 | Measured performance, migration checks, release candidate | v0.21–v0.30 |
| v1.0 | First stable release after all release gates pass | v0.31 |

### v0.21: File fidelity and CI

**Outcome:** supported formats can be edited without losing information, with automated protection against regressions. **References:** REL-01 in [known issues](../known-bugs.md).

- [ ] Confirm the CI workflow (`.github/workflows/ci.yml`: build, Core and Avalonia tests with coverage reports on Windows, macOS and Linux) passes on its first pull request, then make it a required check. Coverage stays a report, not a release criterion.

**Completion gate:** every format advertised as editable passes its documented fidelity fixtures, and CI catches the known regression cases.

### v0.22: Safe saves and recovery

**Outcome:** users can preserve incomplete work and recover from interrupted persistence.

**Completion gate:** failure scenarios preserve a recoverable copy and never report unsaved work as saved.

### v0.23: Extension platform

**Outcome:** a connector can be built, installed, configured, and used without modifying Toucan's application code, so an optional commercial edition can ship as separate packages on the public SDK. Plugins today extend formats, translation providers, validation rules, and framework detection; this release extends the user workflow. Restart stays required for installing and updating plugins.

- [ ] Separate registration from activation: register capabilities without network access, activate per project or connection, give each connection its own session, cancel and dispose on project close, and report activation failures separately from registration failures. Keep application, workspace, connection, and panel lifetimes explicit.
- [ ] Add a command registry with stable IDs, localizable titles and categories, availability conditions, asynchronous execution with cancellation and progress, default shortcuts with user overrides, and menu, toolbar, context-menu, and command-palette contributions. Replace the static table in `KeybindingService` and distinguish hidden, unavailable, disconnected, and unlicensed states.
- [ ] Add a separate desktop contribution package (`Toucan.Plugins.Avalonia`) so the CLI never loads UI assemblies. Replace the hard-coded panel and toolbar switches in `MainWindow` with registered side panels, panel toolbars, settings pages, dialogs, inspector sections, and editor actions; give each plugin view model a narrow workspace context instead of `MainWindowViewModel`. Define supported theme resources, icons, localization, and accessibility conventions, and handle shared Avalonia assemblies in the loader.
- [ ] Provide supported host services: plugin-scoped settings and writable storage kept outside the trusted plugin directory, connection-scoped secret references, background operations with progress, notifications, redacted diagnostics, and typed configuration fields with validation, defaults, scopes, and schema migrations. Document that in-process plugins are not sandboxed.
- [ ] Add a controlled workspace API: readable snapshots and edit transactions for values, comments, review state, and keys, routed through normal undo, dirty tracking, validation, and persistence. Do not expose mutable store internals or view models.
- [ ] Extend compatibility checks to host requirements, desktop-contract version, platform support, and configuration migrations, with actionable failure messages.
- [ ] Ship an SDK test harness (registration and activation, workspace edits, cancellation, CLI operation without desktop dependencies) and a sample connector in a separate project that references only published SDK packages: a connection form, a panel, pull/push commands, and safe workspace changes.
- [ ] Move existing built-in commands and panels onto the new registration paths as each is introduced, so plugins do not get a weaker parallel system.

**Completion gate:** the sample connector, built outside this repository, works on desktop and in the CLI; its command appears in the palette and a menu, supports shortcut reassignment, and becomes unavailable when its workspace closes; and a plugin batch edit behaves like a native edit through undo, save failure, recovery, and reopen.

### v0.24: Editor comfort and onboarding

**Outcome:** a new user can open a project and complete a keyboard-driven translation session comfortably. **References:** APP-02, APP-03.

- [ ] Complete first-run setup for default language, theme, and optional translation-engine credentials; add actionable empty states and a dismissible feature tour.
- [ ] Add reliable inline editing, side-by-side source/target comparison, content-sized multiline fields, and character/word counts.
- [ ] Make bulk operations (copy to language, replace all, pre-translate, move, delete) undoable as one step; undo covers single value edits only today.
- [ ] Make the edit → accept suggestion → approve → next workflow predictable, including focus retention and configurable shortcuts with conflict detection.
- [ ] Clarify source/target hierarchy, placeholder rendering, descriptions, source references, and suggestion provenance in the editor and inspector.
- [ ] Add notification history with actionable save, validation, and integration failures; distinguish progress, cancellation, and completion.
- [ ] Verify light/dark themes, narrow windows, text scaling, keyboard focus, screen-reader labels, and RTL content on supported platforms.

**Completion gate:** a user can work through 100 strings without losing their place or needing a mouse for routine actions; accessibility and layout issues found in that flow are resolved.

### v0.25: Terminology and validation

**Outcome:** translations follow project vocabulary and preserve executable message syntax.

- [ ] Add a project glossary with preferred and forbidden terms, per-language translations, descriptions, and CSV import/export.
- [ ] Show selected-string glossary matches in a Dictionary panel and suggest glossary terms while editing (APP-01).
- [ ] Add configurable glossary validation and pass relevant terminology to machine/AI translation requests.
- [ ] Add debounced validation while typing, with clear severity and navigation to affected fields.
- [ ] Parse and validate supported message syntaxes, including nested ICU plural/select messages, required branches, and placeholders; document syntax/version limits.
- [ ] Finish AI finding re-translation and configurable analysis strictness, retaining preview and explicit acceptance.

**Completion gate:** glossary and message fixtures detect meaningful errors without blocking draft saves; accepting a suggestion follows normal undo and dirty tracking.

### v0.26: Review correctness

**Outcome:** approval reflects the actual source and target that were reviewed.

- [ ] Persist Draft → Needs review → Approved per translation unit and target language, with migration from the existing approval flag.
- [ ] Record the source revision/hash used for review; mark affected targets Needs review when the source changes, and invalidate approval after target edits.
- [ ] Add status filters and row badges; extend existing batch approval/rejection with comments and consistent validation policy.
- [ ] Persist review history with timestamp, action, and available local actor information; do not imply authenticated team identity.
- [ ] Track delivery separately from review: pending, synchronized, failed, or included in an export, applying the `requireValidForApproval` policy to delivery as well; preserve review meaning when remote platforms have different status models.

**Completion gate:** source and target changes cannot leave stale approvals, and review state survives save/reopen and project migration.

### v0.27: Content sources and snapshot mode

**Outcome:** a shared workspace can load and persist content through source adapters without changing translation engines.

- [ ] Define content-source contracts separately from `ITranslationProvider` and AI backends; expose optional read, snapshot-write, CRUD, status, revision, and delta capabilities.
- [ ] Add connection profiles with stable provider and connection IDs, project selection, secret references, permissions, and a connection test; support multiple profiles for one service.
- [ ] Extend workspace identity and persistence (building on the v0.23 workspace API) for connection, package/resource scope, source/target locales and text, stable unit IDs, remote IDs/revisions, and preserved format metadata.
- [ ] Implement the local-folder snapshot adapter over existing format strategies, preserving current projects and CLI behavior.
- [ ] Support initial snapshot loading, cached opening, explicit refresh, and save-plan previews; keep loading policy separate from persistence mode.
- [ ] Add contract and migration tests for read-only sources, unsupported capabilities, duplicate key names across packages, and existing plugin compatibility; version new public contracts deliberately.

**Completion gate:** local projects work through the source boundary without fidelity regressions, credentials in project files, or changes to translation-engine responsibilities.

### v0.28: CRUD and synchronization engine

**Outcome:** local edits become durable, reviewable operations that can be reconciled with remote changes.

- [ ] Define separate operations for key creation, translation creation/update, rename, translation deletion, and whole-key deletion; enforce provider capabilities and permissions.
- [ ] Persist a synchronization baseline and pending-operation journal, isolated from local-save baselines; edit locally without issuing a request on each keystroke.
- [ ] Extend three-way comparison to stable identity, source/target text, supported metadata and status; handle concurrent creation and edit-versus-delete conflicts.
- [ ] Add a conflict-resolution view and explicit pull/push plans; preserve unrelated local edits during refresh and require deliberate inclusion of remote deletions.
- [ ] Support conditional writes where available, per-operation results, partial success, safe retry/reconciliation, and asynchronous-job completion; disclose weaker concurrency guarantees where necessary.
- [ ] Verify restart recovery, ambiguous network failures, duplicate-prevention, pagination completeness, and cancellation with a deterministic test adapter; never infer deletion from an incomplete read.

**Completion gate:** failed or repeated synchronization preserves unsent work, avoids duplicate writes, and cannot silently overwrite detected concurrent edits.

### v0.29: First online connector

**Outcome:** users can complete one real online workflow from connection through delivery.

- [ ] Select one service from actual user demand, such as Crowdin, Lokalise, or a documented custom REST service; specify whether its adapter uses snapshots, CRUD, or both.
- [ ] Treat installation, trust, activation, and entitlement as separate states: add staged plugin installation with integrity checks and rollback, publisher signature verification for a commercial channel (keeping an explicit community/development path), and an entitlement check with defined offline and expiry behavior that keeps cached work and export available. Keep service authentication separate from Toucan licensing.
- [ ] Implement authentication, project/resource/language mapping, complete paginated reads, and the supported write operations against that service.
- [ ] Add bounded concurrency, timeout/cancellation, rate-limit handling and backoff, token-expiry handling, and actionable permission errors.
- [ ] Expose connection settings and status in a Sources area, separate from translation engines and AI; show last successful sync, pending changes, and failures.
- [ ] Support explicit pull/push, offline cached editing, restart recovery, and distinct Saved locally / Synced remotely indicators.
- [ ] Run connector contract checks and controlled end-to-end tests covering changed remote content, partial failures, asynchronous jobs where applicable, and secret redaction in diagnostics.

**Completion gate:** the selected connector passes a full offline/edit/reconnect/conflict/push/reopen workflow. If it cannot meet the safety gates, label it preview and exclude it from stable integration claims.

### v0.30: Distribution and updates

**Outcome:** releases are repeatable, installable, and discoverable. **References:** REL-01, REL-02, REL-03, REL-04.

- [ ] Add tag-triggered release builds with version checks, artifact checksums, and packaged-build smoke tests on supported platforms.
- [ ] Validate and ship a Windows installer alongside the portable package; configure trusted signing for release artifacts where required.
- [ ] Configure Developer ID signing and notarization for macOS; preserve a documented development-build path.
- [ ] Script Linux packaging and document supported architectures, dependencies, desktop integration, and installation/removal.
- [ ] Add configurable startup update checks, stable/preview channels, changelog previews, and a clear download/open-release action.
- [ ] Verify clean install, upgrade, uninstall, and settings preservation; document signing costs or platform limitations that remain unresolved.

**Completion gate:** a release can be built from its tag and installed/upgraded on each supported platform, with explicit platform limitations and no manual artifact patching. Automatic in-app installation is deferred.

### v0.31: Performance and release candidate

**Outcome:** the complete workflow has measured limits and survives upgrade and failure scenarios. **Reference:** QA-02.

- [ ] Profile open, edit, search, save, and sync on named hardware with representative fixtures, including 10,000 keys × 10 locales; record cold/warm timings and memory.
- [ ] Address measured bottlenecks with virtualization, lazy loading, background persistence, indexed search, or memory changes as justified by profiling.
- [ ] Verify editor responsiveness during save/sync and retain accurate cancellation, progress, and partial-result behavior.
- [ ] Test project, settings, connection, review, and recovery migrations from supported preview versions; document backup and downgrade behavior.
- [ ] Run packaged end-to-end checks for open → edit → validate → review → save/sync → close → reopen across supported platforms; repeat accessibility and visual checks for changed screens.
- [ ] Publish a release candidate, document remaining limitations, and resolve release-blocking feedback before v1.0.

**Proposed performance budgets:** usable editor within five seconds for the reference fixture, warm search within 200 ms, and responsive typing while persistence runs. Confirm or revise these budgets against named hardware before claiming them; they are not current measurements.

**Completion gate:** measured budgets are met or the supported scale is explicitly narrowed, migrations pass, and no unresolved data-loss or silent-overwrite issue remains.

## v1.0 release gates

Version 1.0 follows v0.31 only when these acceptance checks pass. Freeze public contracts and persistence schemas after reviewing the supported migration policy.

- [ ] Every format advertised as editable preserves its documented semantics and metadata; unsupported constructs have safe, visible handling.
- [ ] Failed saves, crashes, and interrupted synchronization preserve recoverable work and accurate pending state.
- [ ] Daily editing and review workflows pass automated and packaged-build checks on Windows, macOS, and Linux, including source-change approval invalidation.
- [ ] Stable online connectors handle concurrency, permissions, partial success, and retry/recovery; preview connectors are clearly identified.
- [ ] Installation, update discovery, settings/project migrations, and stable plugin-contract compatibility are tested and documented.
- [ ] Performance and accessibility have recorded verification, known limitations are published, and no release-blocking issue remains.

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
- [ ] Add macOS native menu-bar and system-accent integration.

### Editor, glossary, and reporting

- [ ] Add Markdown preview for translation values.
- [ ] Add glossary TBX import/export.
- [ ] Export translation-progress reports as HTML/PDF and missing/unused-key reports per language.
- [ ] Export changes since a selected date and add a statistics dashboard with trends.

### Custom webhook translation engine (FG-14)

- [ ] Add configurable request-body templates and response-path mapping.
- [ ] Add timeout settings and safe retries for translation requests.
- [ ] Add opt-in on-save webhook notifications with explicit destination and payload configuration.

### Plugin system

Core plugins shipped in v0.18.0. See the [plugin guide](../plugins.md) and [original plan](../archive/plugin-system-plan.md).

- [ ] Add per-project validation rule files under `.toucan/rules/`.
- [ ] Make signing mandatory for all plugins and add a plugin feed alongside the collaboration/auth milestone (commercial-channel verification ships in v0.29).
- [ ] Add per-rule enable/severity settings for plugin rules.

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
