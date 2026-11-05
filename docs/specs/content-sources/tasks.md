---
title: "content-sources — tasks"
status: planned
progress: "1/9 phases"
updated: 2026-11-05
summary: "Work order for content sources across v0.27–v0.29 and v0.31 hardening: prerequisites, API 1.3 contracts, accounts and auth, local-folder adapter, sync engine, desktop UI, reference REST adapter and CLI, first online connector."
---
# Tasks: content sources

Requirements: [requirements.md](requirements.md). Design: [design.md](design.md). Release scope stays in the [roadmap](../../todos/future-roadmap.md#v027-content-sources-and-snapshot-mode); this file is the order of work. Each phase leaves `dotnet test Toucan.CrossPlatform.slnx` green. Remove nothing from here until the matching roadmap task is removed.

## Phase 0: Prerequisites (v0.27) — done 2026-11-05

- [x] 0.1 Add an owning plugin ID to `IPluginActivationService.OpenConnectionAsync` so only that plugin's connection activators run; keep the current overload and mark it obsolete. A connection ID already open for another owner is refused. Tests: two plugins, one connection, only the owner activates. (R2.5) `ProjectLifecycleService` keeps the legacy call for its bare `SourceConnections` IDs until 2.1.
- [x] 0.2 Add `NeedsReview` to the plugin-facing `ReviewState` in API 1.3 and map it to Core `ReviewStatus.NeedsReview` in both workspace APIs; 1.2 plugins keep seeing `Draft`. The workspace engine's units now carry the needs-review flag through edits, projections and undo. Document-aware sidecars still do not persist it (roadmap v0.26). (design §7.9)
- [x] 0.3 Add the `sources` capability to `PluginCapabilities` and the optional `network` manifest block (`hosts`, `userDefinedHosts`) with validation; include `network` in the trust hash and the trust prompt. (R4.2)
- [x] 0.4 Move the AES key behind `secrets.json` into the OS store (`IMasterKeyStore`: macOS Keychain via Security.framework, Linux Secret Service via `secret-tool`); Windows keeps DPAPI. Migrate an existing key file on first use, fall back to the file without a store, pin to the store with a marker so a locked store never creates a second key, and allow `TOUCAN_SECRET_KEY_STORE=file`. Real-Keychain test is opt-in (`TOUCAN_TEST_KEYCHAIN=1`); Linux is covered with a fake store only. Decision D2.
- [x] 0.5 Bump `PluginApi.Current` and the abstractions package to 1.3. Compatibility: the frozen M0 plugin binary (API 1.1) still loads and saves unchanged, the version check accepts 1.0–1.3, and 1.0/1.2 workspace views are covered by the needs-review downgrade tests. No frozen 1.0 or 1.2 binary exists to load.

## Phase 1: Contracts and test kit (v0.27)

- [ ] 1.1 Add `Toucan.Plugins.Abstractions/Sources/`: adapter, descriptor, capabilities, value model, session context, `IContentSourceSession`, `IUnitSourceSession`, `IDocumentSourceSession`, the optional interfaces, write operations and results, `SourceError`/`SourceException`. (R1.1–R1.3)
- [ ] 1.2 Add `IPluginContext.AddSourceAdapter` with validation matching `AddFormat` (ID prefix, reserved IDs, descriptor checks, capability present); registration catalog in Core; list adapters in `toucan plugins list` and Settings → Plugins. (R1.5)
- [ ] 1.3 Add `FakeSourceServer` to `Toucan.Plugins.Testing` with unit and document modes, revisions, pagination and fault injection (429, 5xx, timeout after commit, dropped page, duplicates, concurrent edits, token expiry, async jobs). (R12.1)
- [ ] 1.4 Add the `SourceAdapterConformance` suite skeleton (pagination completeness, `IsComplete` honesty, idempotency, conflict reporting, error mapping, cancellation) and run it against a fake adapter.
- [ ] 1.5 Public API docs: extend [plugins.md](../../plugins.md) with a content-source section and an adapter author guide outline.

## Phase 2: Accounts, connections and auth (v0.27)

- [ ] 2.1 Account store (`accounts.json` in app data) and connection model; add `sources` to `toucan.tproj` and `toucan.project.schema.json` with a schema migration test. Replace `ProjectSettings.SourceConnections` with these records and switch `ProjectLifecycleService` open and replace to the owner-aware `OpenConnectionAsync`, removing its CS0618 suppressions. (R2.1–R2.3, R2.5)
- [ ] 2.2 `SourceConnectionService`: resolve accounts on open, lazy session opening, `connectOnOpen`, connection-scoped teardown on close, replace and remove; failure isolates one connection. (R2.4, R2.5)
- [ ] 2.3 `SourceCredentialBroker`: storage under `credentialRef`, refresh with a cross-process lock, redaction registration, sign-out and revocation. (R3.2–R3.4)
- [ ] 2.4 Auth flows: token, basic, OAuth authorization code with PKCE over a loopback redirect, device code, custom exchange; unit tests against a fake authorization server. (R3.1)
- [ ] 2.5 `ISourceHttpClientFactory` handler pipeline: host allow-list, HTTPS, auth with one refresh on 401, rate limiting, safe retry, timeouts, redacted logging, proxy and user agent. (R4.1)
- [ ] 2.6 Mapping model and validation: language map with suggestions, resource map, key separator and escaping, collision refusal. (R5.1–R5.3)
- [ ] 2.7 CLI: `toucan sources list`, `accounts`, `login` (device code, `--token-stdin`, env var), `test`. (R3.5, R10.1)

## Phase 3: Local-folder adapter and remote document store (v0.27)

- [ ] 3.1 `RemoteWorkspaceDocumentStore` and `RemoteWorkspaceSourceReader` wrapping an `IDocumentSourceSession`, so `WorkspaceDocumentSavePlanner` and format strategies work over a remote store.
- [ ] 3.2 Built-in module `Toucan.Modules.Sources` with the local-folder adapter (`toucan.sources.local`) over the existing local reader and store.
- [ ] 3.3 Open existing fixture projects through the local-folder adapter and compare against the direct path: identical snapshots, identical save bytes, unrelated files untouched. Include the babeledit-parity fixtures. (R11.1)
- [ ] 3.4 Snapshot loading, cached opening and explicit refresh for role `primary`; save-plan previews against a document source.
- [ ] 3.5 Contract tests: read-only source, unsupported capabilities refused before execution, duplicate key names across packages, older plugins unaffected.

## Phase 4: Sync engine (v0.28)

- [ ] 4.1 `SyncStateStore`: `baseline.json`, `journal.json`, `cache/` under `.toucan/local/sources/{connectionId}/`, generated `.gitignore`, atomic writes, schema versioning, corrupt-file quarantine. (R6.1, R6.4)
- [ ] 4.2 `SyncPlanner`: pure three-way comparison per field following design §7.2, rename matching, deletion rules, first-sync mode, capability and permission checks producing notices. Table-driven tests for every row. (R7)
- [ ] 4.3 Pull application through `WorkspaceService` as one `Source`-origin transaction with `ExpectedValue`, baseline advance for applied units only, review withdrawal through `ReviewTracker`. (R8.1, R8.2)
- [ ] 4.4 `SyncExecutor` push path: journal before send, batches and bounded concurrency from `SourceLimits`, per-result handling, reconciliation of `Unknown` and `InFlight` by reading first, async job polling. (R6.2, R6.3, R8.3)
- [ ] 4.5 Delivery state per unit from journal and baseline; expose it in identified snapshots and subscription deltas. (R8.4)
- [ ] 4.6 `SyncCoordinator`: one run per connection, coalescing, background operations with progress and cancel, status events; opt-in scheduled check that plans without applying.
- [ ] 4.7 Multiple connections: separate state, `primary` uniqueness per unit, sequential planning for overlapping units.
- [ ] 4.8 `ISourceSyncApi` on `IPluginServices` (read status, request plan or run; no state writes).
- [ ] 4.9 Recovery and failure suite against `FakeSourceServer`: restart from every journal state, ambiguous failures, duplicate prevention, dropped pages never inferring deletion, cancellation mid-batch, token expiry mid-run, undo of a pull, Save As with connections. (R12.2)

## Phase 5: Desktop experience (v0.28)

- [ ] 5.1 Sources side panel and Settings page with connection states, last pull and push, pending counts and actions. (R9.1)
- [ ] 5.2 Accounts page: add, sign in again, sign out, usage by project.
- [ ] 5.3 Add-connection wizard: adapter, account and auth, project picker, mapping with suggestions and collision errors, role and policy, test, optional first pull.
- [ ] 5.4 Plan preview dialog with grouping, include toggles, counts and virtualization.
- [ ] 5.5 Conflict view with base, local and remote columns, Take local, Take remote, Edit merged, Defer; reachable from row badges.
- [ ] 5.6 Row delivery badges and separate Saved locally / Synced status bar indicators. (R8.5)
- [ ] 5.7 Built-in `sources.*` commands in palette and menus with reassignable shortcuts; notifications for expired sign-in, partial failure, remote changes available. (R9.2)
- [ ] 5.8 Keyboard, screen reader and visual review checks for all new screens. (R9.3)

## Phase 6: Reference REST adapter, sample port and full CLI (v0.28)

- [ ] 6.1 Write the Toucan source protocol: OpenAPI file and prose spec under `docs/` (ETags, cursor pagination, idempotency keys, delta endpoint, error format). Decision D5.
- [ ] 6.2 Reference server in `samples/` and the built-in `toucan.sources.rest` unit adapter; run the conformance suite against it in CI. (R11.2)
- [ ] 6.3 Port `samples/Toucan.Sample.Connector` from `IRemoteSource` to the public contract as the minimal third-party example; keep its manifest on API 1.3. (R11.3)
- [ ] 6.4 CLI `status`, `pull`, `push`, `sync` with `--plan`, `--yes`, `--take`, `--json`, non-interactive refusal and exit codes; save after pull through the document coordinator. (R10)

## Phase 7: First online connector (v0.29)

- [ ] 7.1 Decide the service (D1) from user demand and record whether it uses the unit model, the document model or both.
- [ ] 7.2 Implement the adapter as an external plugin: auth, project discovery, mapping hints, complete paginated reads, supported writes, status mapping, error mapping, limits.
- [ ] 7.3 Pass the conformance suite with recorded fixtures; add opt-in live tests against a sandbox project.
- [ ] 7.4 End-to-end workflow: connect, first pull, offline edit, restart, reconnect, remote concurrent edit, conflict, push, reopen; secret redaction in a diagnostics report. Label the connector preview if any step fails. (R11.4)
- [ ] 7.5 Adapter author guide with the walkthrough of this connector and the sample.

## Phase 8: Hardening (v0.31)

- [ ] 8.1 Profile pull, plan and push at 10,000 keys × 10 locales; record timings and memory; keep the editor responsive during sync.
- [ ] 8.2 Migration checks for `sources` in project files, accounts, baselines and journals from each supported preview version.
- [ ] 8.3 Packaged end-to-end checks on Windows, macOS and Linux including the OS credential stores.
- [ ] 8.4 Update roadmap, `CHANGELOG.md`, `completed-features.md`, README and site as each phase ships.
