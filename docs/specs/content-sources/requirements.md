---
title: "content-sources — requirements"
status: planned
progress: "0/12 requirements met"
updated: 2026-11-05
summary: "Requirements for content-source adapters: plugins that sign in to a service and read or write the open project's translations, with host-owned credentials, sync state, plans, conflicts and recovery."
---
# Requirements: content sources

Design: [design.md](design.md). Work order: [tasks.md](tasks.md).

## Introduction

Toucan can host a plugin that edits the open project, and the sample connector shows pull and push against an in-memory service. There is no public contract for a source, the host does not open connections, and there is no login helper, sync baseline, pending-operation journal or conflict view. Each connector would have to rebuild all of it, with its own failure modes.

This work adds content sources as a first-class plugin type and moves every safety-critical part (credentials, sync state, merge decisions, recovery) into the host. It covers roadmap milestones v0.27, v0.28 and v0.29.

## Glossary

See [design §3](design.md#3-concepts-and-vocabulary). In short: an **adapter** talks to one service type; an **account** is a user's sign-in to a service instance; a **connection** binds a project to a remote project through an account; a **baseline** is the last agreed state; a **journal** holds unconfirmed outbound operations; a **plan** is a previewable set of changes.

## Requirements

### R1: Public adapter contract

1. The plugin API SHALL expose a content-source adapter contract in `Toucan.Plugins.Abstractions`, separate from `ITranslationProvider` and AI backends.
2. The contract SHALL support unit-level services (key/value APIs) and document-level services (whole files with versions).
3. Adapters SHALL declare capabilities; the host SHALL NOT send an adapter an operation outside its declared capabilities or the connection's permissions.
4. The change SHALL be a minor API version (1.3). Plugins built for 1.0–1.2 SHALL load and behave unchanged.
5. Registration SHALL perform no network or disk I/O beyond the plugin's own files.

### R2: Accounts and connections

1. Accounts SHALL be stored per user, outside any project, and be reusable across projects.
2. Connections SHALL be stored in the project file with stable IDs and no credentials, so a team can share them.
3. A project SHALL support several connections, including several for the same service.
4. Opening a project SHALL NOT block on the network. A connection without a signed-in account SHALL remain visible and the project SHALL remain editable.
5. The host SHALL open and close connection scopes itself, activating only the owning plugin.
6. Connection setup SHALL include a connection test and remote project selection where the service allows listing.

### R3: Authentication

1. The host SHALL run token, basic, OAuth authorization code with PKCE, OAuth device code and adapter-defined exchange flows.
2. Credentials SHALL be kept only in the secret store, preferring the operating system's credential store.
3. Access tokens SHALL refresh automatically; concurrent refreshes for one account SHALL be serialized across processes.
4. Expired or revoked sign-ins SHALL prompt the user to sign in again without losing pending work.
5. The CLI SHALL authenticate through device code or a token from stdin or an environment variable, without writing that token to disk.

### R4: Network safety

1. The host SHALL provide an HTTP client to adapters that enforces declared hosts, HTTPS, timeouts, cancellation, rate limits, safe retries and redaction.
2. A plugin with the `sources` capability SHALL declare its network hosts in its manifest, and the trust prompt SHALL show them.
3. Secrets SHALL NOT appear in logs, diagnostics, notifications, error messages or project files.

### R5: Mapping

1. Connections SHALL map project languages to remote language codes explicitly, with suggestions and no silent matches.
2. Connections SHALL map remote resources to packages or documents and remote keys to namespace and key paths.
3. Setup SHALL refuse mappings where two remote keys resolve to one entry.
4. After the first sync, identity SHALL follow stable remote IDs and entry IDs, so renames are detected as renames.

### R6: Sync state and recovery

1. The host SHALL keep a per-connection baseline and journal, separate from local-save baselines and never committed to version control.
2. Every outbound operation SHALL be journaled before it is sent and carry an idempotency key.
3. After a crash, restart, cancellation or ambiguous network failure, the host SHALL reconcile in-flight operations by reading remote state before retrying, and SHALL NOT create duplicates.
4. A corrupt or unknown baseline SHALL fall back to first-sync behavior, never to overwriting.

### R7: Comparison and plans

1. Pull, push and sync SHALL compute an immutable plan from a captured workspace snapshot and a remote read, and SHALL show it before applying.
2. Comparison SHALL be three-way per field (value, review status, comment, existence, key path).
3. Concurrent changes to the same field SHALL become conflicts; detected concurrent edits SHALL NOT be overwritten silently.
4. Remote deletions SHALL be inferred only from a complete read and SHALL require explicit inclusion. Local deletions SHALL be pushed only when the connection's policy allows.
5. The first sync without a baseline SHALL treat every difference as a conflict with explicit bulk choices.

### R8: Applying changes

1. Pulled changes SHALL enter the workspace as one undoable transaction with origin `Source`, refusing units the user edited after the plan was captured.
2. Pulled value changes SHALL withdraw stale approvals through the existing review rules.
3. Pushes SHALL report per-operation results, including partial success, and SHALL keep failed units pending.
4. Each unit SHALL have a delivery state (pending, sending, synced, failed, conflict) separate from its review state.
5. Saving locally and synchronizing remotely SHALL be independent and shown separately.

### R9: Desktop experience

1. The desktop app SHALL provide a Sources area, an accounts page, an add-connection wizard, a plan preview, a conflict view, per-row delivery badges and status bar indicators.
2. Pull, push, sync and conflict commands SHALL appear in the command palette and menus with reassignable shortcuts.
3. New screens SHALL meet the existing keyboard and accessibility checks.

### R10: CLI

1. The CLI SHALL list, test, sign in to, plan, pull, push and sync connections, with JSON output.
2. A non-interactive CLI SHALL refuse to apply a plan without `--yes`, and SHALL never resolve conflicts unless told how.
3. Exit codes SHALL distinguish success, conflicts, authentication required and partial failure.

### R11: Reference adapters and the first connector

1. A local-folder adapter SHALL open existing projects through the source boundary without fidelity regressions.
2. A reference REST adapter SHALL implement a documented protocol, with a reference server used in tests.
3. The sample connector SHALL be ported to the public contract.
4. One commercial service SHALL be supported end to end: connect, pull, offline edit, reconnect, conflict, push, reopen. If it cannot meet these requirements, it SHALL be labeled preview.

### R12: Testability

1. `Toucan.Plugins.Testing` SHALL provide a fake source server with fault injection and an adapter conformance suite that third-party authors can run.
2. The sync engine SHALL be covered for every journal state on restart, ambiguous failures, pagination gaps, cancellation and multiple connections.

## Out of scope

- Automatic background sync and change-feed driven apply (post-1.0; the contracts ship now).
- Declarative REST mappings (post-1.0).
- Real-time collaboration, presence and locking.
- Plugin sandboxing. Signing and entitlements are tracked in v0.29 and are not designed here.
- Embedded web-view login.

## Open decisions

| ID | Decision | Default if not decided |
|---|---|---|
| D1 | First commercial connector | Lokalise (unit model, public OAuth); Crowdin second for the document model |
| D2 | Whether moving secrets to the OS credential store ships in v0.27 or later | Ship in v0.27, with the encrypted file as fallback |
| D3 | Sync state location | `.toucan/local/sources/` with a generated `.gitignore` |
| D4 | Whether pushes require a local save first in `mirror` role | No; delivery and save are independent, and the UI shows both |
| D5 | Whether the Toucan source protocol is published as a standalone spec for third-party servers | Yes, under `docs/` with an OpenAPI file |
