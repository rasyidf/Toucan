---
title: Workspace sync engine design
status: in-progress
progress: "Phases 1–5 implemented; format migration and integration coverage open"
updated: 2026-10-29
summary: "Core engine, targeted saves, destination choices, navigation, conflict presentation, lifecycle, and headless saves are implemented. Format migration and integration coverage remain."
---
# Workspace sync engine design

Implementation tracker: [Workspace sync engine tracker](../../todos/workspace-sync-engine.md).

## Implementation status

Phase 1 is implemented through the opt-in `WorkspaceSourceLoader` and `WorkspaceDocumentRouter` services registered in Core. Immutable source snapshots retain document ownership, versions, original content, namespace/key segments, and identity checkpoints. JSON and namespaced JSON expose `IDocumentLoadStrategy`; i18next exposes `IFrameworkDocumentLayout`. Both optional interfaces use existing format/framework registration and capability checks. The plugin API/package minor version is 1.2; older 1.0/1.1 plugins remain supported.

Phase 2 adds `WorkspaceService`, which owns immutable committed state, revisions, dirty baselines, approval policy, and bounded transaction history. Native edits commit changes from desktop projections; plugins apply Core commands through the desktop adapter. Undo/redo restores values, comments, review state, additions, deletions, and renames using entry identities. The desktop no longer records its own history. Standalone row controls retain the legacy value-history option for compatibility.

Core provides a headless workspace backend. The desktop adapter schedules pending-text flushes and presentation refreshes on its dispatcher. `ProjectLifecycleService` opens Core state before workspace activators run, cancels and cleans up cancelled activation, retains read access during plugin teardown, rejects new closing-session commands, and clears Core state afterward. Cancelled close retains scopes and history. Save As closes the old plugin scope and opens a new workspace generation at the destination. Legacy synchronous persistence captures Core state under a mutation lease and acknowledges it after successful writes; its change notifications are delivered after the lease ends.

Plugin API 1.2 also offers `IIdentifiedWorkspaceApi` with immutable entry snapshots and atomic ID-based edits. Entry edits carry workspace session identity and revision; stale or ambiguous legacy edits are refused. Legacy validation rules run ambiguous entries in separate contexts. Observer failures are logged without invalidating accepted changes.

The desktop still uses legacy file loaders and serializers. Compatibility `TranslationItem` projections remain in the editor and persistence adapters; they are not canonical Core state. The document-aware loader remains opt-in until targeted persistence is integrated. Async subscriber streams coexist with legacy change notifications. Source connection adapters and complete source lifecycle handling remain tracked work. The stateless source loader/router perform no activation or writes.

### Document save planning implemented in Phase 4

`IWorkspaceSourceReader` separates source reads from discovery/layout and serialization. The default local reader retains a byte fingerprint, decoded content, encoding name, and BOM flag. It is stateless and registered at application scope without workspace I/O; callers retain source snapshots in their workspace/connection scope. `IDocumentSaveStrategy : ISaveStrategy` is an optional API 1.2 capability registered through the existing format registration. JSON and namespaced JSON implement pure, single-document serialization; there is no fallback to their legacy bulk writers.

`WorkspaceService.CaptureDocumentSnapshot()` captures entry IDs, literal paths, source bindings, values/raw scalar metadata, session, and revision under the state gate. `WorkspaceDocumentSavePlanner.Plan` compares this immutable content snapshot with its loaded source and returns declared create/update/delete targets with expected versions, serialized content, and encoding/BOM metadata. It does not write, acknowledge a saved baseline, or replace the source. Unchanged documents produce no targets. Removing the last translation preserves the document and unedited constructs; deleting the file requires an explicit document deletion request. Renames retain ownership. Moves and namespace changes require an explicit destination; new entries can use a unique existing destination or a declared new one. Destination collisions, incompatible languages/namespaces, escaped paths, and claimed original bindings are refused.

New document destinations require `IFrameworkDocumentLayout.DescribeDestination(root, relativePath)`, whose default null means the framework cannot guarantee discovery on reopen. i18next describes its supported single-language-file and language/package-folder layouts without filesystem access. Existing plugins remain compatible; a profile without this optional description can still read and edit its discovered documents but cannot plan new document creation. A document plan does not invoke framework discovery or `GetFilePath` during planning.

JSON value-only edits replace only the addressed scalar tokens, retaining surrounding whitespace, escapes, property order, arrays, nulls, and empty containers exactly. Structural object edits preserve unedited values/raw numbers and retain indentation, line endings, and a trailing newline while reserializing the affected document. Deleting or moving an array slot is refused until explicit array operations can preserve sibling identities. Plans include affected per-document metadata, identity catalogs, and changed manifests. Explicit framework projects with document read/write capabilities use this pipeline in the desktop; other formats retain their declared legacy bulk-save path. New-entry destination choices still need app UI integration.

### Document content execution and recovery implemented in Phase 4

`WorkspaceDocumentSaveCoordinator` captures and validates the current workspace, plans its content, links the caller's cancellation token to the workspace session, executes the plan, and acknowledges its receipt. The services are stateless between operations and registered at application scope without I/O. Each execution owns and disposes its file lock and streams; the workspace owns session cancellation and committed state. Registration does not activate a framework, source connection, or plugin. The desktop has not switched to this pipeline.

`WorkspaceDocumentSaveExecutor` verifies that serialized documents round-trip the declared units before writing. `IWorkspaceDocumentStore` separates conditional mutations from layout/serialization; the local implementation retains source encoding/BOMs, stages atomic writes, and checks SHA-256 versions immediately before replacement. Creates require missing targets and use a non-overwriting move; updates require existing targets and use replacement. Deletes check their expected version. These are optimistic filesystem checks; unrelated external writers must cooperate to eliminate the check/replacement race. Symbolic-link targets and linked destination directories are refused. A per-root file lock rejects overlapping document executions; manual rollback/discard takes the same lock. Unresolved recovery journals cannot be overwritten.

A successful receipt contains the captured plan and a refreshed source snapshot with actual written versions, parsed raw scalar types, updated bindings, and an updated in-memory identity catalog. Structural identity changes persist through `.toucan/workspace-identities.json` in the same declared-target transaction. A no-change save writes no documents and creates no recovery directory. Acknowledgment requires a snapshot owned by the active workspace session; it advances the baseline to the captured state, preserves later edits, and publishes a revisioned `Saved` event. Live units and undo/redo history rebase their source bindings to the saved addresses. Restoring an entry after its deletion was saved makes it a new insertion. Old-session or replayed receipts cannot mark a reopened workspace clean.

Comments, approvals, and edit metadata are planned in `.toucan/entries/{documentId}.json` sidecars only for affected owners. Concurrent metadata edits after capture stay dirty. The standalone executor returns a receipt without automatically acknowledging workspace state; callers must use the coordinator or validate and acknowledge explicitly.

Declared-target saves use recovery journal schema 2. Each target records its original byte version and a durable intended post-write version before mutation. Rollback restores only targets matching the save's written version, skips untouched targets, and retains externally changed content with a recovery error and journal. It never scans for new files or removes unrelated concurrent creations. An explicit conditional-write refusal relinquishes rollback ownership of that target. Unversioned/schema-1 journals retain legacy recovery behavior. Unknown schemas remain untouched and block new document saves. Identity catalogs and schema 2 recovery drafts are integrated with desktop recovery. Draft-only identity fallback is implemented; remaining recovery/lifecycle integration tests stay tracked.

Coverage includes no-change I/O, untouched hashes/timestamps, creates/updates/deletes, interrupted recovery, partial failures, concurrent saves and edits, close/reopen cancellation, external changes before/during/after mutation, metadata safeguards, raw types, undo/redo rebasing, encoding/BOMs, and legacy journal compatibility.

### Destination choices implemented in Phase 4

`WorkspaceDocumentRouter.DestinationFamilies` groups documents by framework layout family (one `checkout.json` across languages) and places each requested language. It uses an existing document, a sibling path with the language segment or file stem substituted, or the framework default. Every proposed document must pass `DescribeDestination` with the same package and family. Languages the layout cannot place are omitted. It reads no files.

Workspace units carry an optional destination document, captured in `WorkspaceDocumentUnit.Destination`, so undo/redo and recovery drafts keep it with the translation. `AddDocumentEntry`, `MoveDocumentEntry`, `AddDocumentLanguage` (optionally for selected entries), and `RemoveDocumentLanguage` commit native transactions. The planner treats captured destinations as explicit; caller-supplied destinations take precedence. Acknowledgment clears a destination once the captured choice was written. Removing a language records it, and the snapshot then declares that language's source documents as deletions while it has no translations. Undo restoring them cancels the deletion, and recovery drafts carry removed languages.

The desktop asks for a destination only when more than one family can hold a new key. Duplicating keeps literal-dot key paths. Adding, removing, and managing languages edit the workspace; documents are created or deleted by the next save. The CLI `set` and `translate` commands open the workspace for document projects, add missing languages to existing entries, and save through the coordinator instead of the legacy bulk writer.

### Save scopes declared in Phase 6

`ISaveStrategy.SaveScope` declares `Project` (a save rewrites every owned file) or `DeclaredDocuments`. The default is `DeclaredDocuments` only for an `IDocumentSaveStrategy`. Capability resolution uses targeted saving only when the writer declares it, so a document writer can opt out until verified. JSON and namespaced JSON are the verified built-ins, pinned by a guard test.

### Desktop persistence, navigation projections, and source refresh

`ProjectService` resolves explicit framework/format capabilities before loading; unavailable capabilities fail without fallback. `ProjectLifecycleService` saves captured document plans and acknowledges them on the presentation dispatcher. Save As uses a separate create-only export plan, retains source files, and creates a new workspace/plugin session at the destination. Edits newer than the export capture remain dirty in the new session.

`WorkspaceNavigator` builds namespace-first and file-first trees from immutable identified snapshots and source origins. Selection uses entry IDs; the projections do not route persistence. Navigation node IDs use stable bounded fingerprints, avoiding identifier growth in deeply nested paths. Core tests confirm both layouts produce identical save targets. The desktop Explorer exposes both layouts. Editor cards group by entry ID and display source file/pointer origins. Navigator and editor layout changes flush pending text; entry selection survives layout changes and structural refresh. Document plurals remain separate identified entries; legacy projects retain their existing plural-card grouping. Explorer context menus, rename/delete, and per-key editor actions operate on the selected identity. Card labels and rename inputs retain literal key segments, including dots. Source language creation/removal and literal-dot duplication use destination families (see above).

The desktop watcher reports changed relative paths with byte fingerprints, including owned metadata. Targeted saves acknowledge only committed target versions, preserving unrelated external edits and detecting later changes to saved files. Watch generations and lifecycle root/session checks reject stale work after replacement. Core source refresh replaces clean documents and rebases history while retaining dirty conflicts and their original versions. The app still needs document-level conflict presentation and full connection/shutdown coverage.

### Identity policy implemented in Phase 1

Document IDs derive from normalized workspace-relative paths and are scoped to a workspace. Entry IDs derive from structured addresses within either an explicitly declared framework resource family or an individual document. Reopening unchanged addresses recreates the same IDs; separate locale roots and undeclared document relationships remain distinct.

An in-memory rename retains `EntryId` and its source binding. Update addresses in `WorkspaceSource.IdentityCatalog` while retaining IDs after a rename/move, wrap the catalog in `WorkspaceIdentityCheckpoint(source.Root, entries)`, and pass that checkpoint to `LoadAsync` during reopen or recovery. Checkpoints are rejected for a different source root. Document saves persist identity catalogs and structured schema 2 drafts retain checkpoints with baseline/current entries. Recovery restores structural edits as one undo transaction and refuses changed baselines. When no durable catalog exists, project loading can use the root/framework/format-matched schema 2 draft checkpoint. A durable catalog takes precedence over the recovery fallback. Restart recovery with custom IDs preserves the baseline and refuses external changes; a later save persists the identities with the documents. Legacy saves retain their existing identity behavior. Whole-workspace generations now change on open and Save As.

Phase 1 coverage includes duplicate keys, language variants in separate files, literal dots and escaped source locations, raw scalar metadata, identity checkpoint serialization, ambiguous routing, missing capabilities, cancellation, and third-party profiles without grouping extensions.

## Goal and scope

Move workspace ownership into an in-process Core service. The app publishes edit commands and asynchronously subscribes to accepted changes. Source adapters, navigators, and presenters connect independently, so the same workspace supports namespace-first and file-first navigation without changing save destinations.

The first delivery covers engine extraction and targeted persistence for a representative multi-file JSON layout, followed by both navigation projections. Another executable and an online connector are outside this first delivery.

```text
Presenter -- edit commands --> Workspace engine -- save plan --> Source adapter
Presenter <-- change stream - Workspace engine <-- changes ---- Source adapter
                                  |
                         Navigator projections
```

## Original boundaries addressed by this plan

- `Toucan.Avalonia/Services/WorkspaceBackend.cs` delegates workspace operations to `MainWindowViewModel` and dispatches calls onto the UI thread.
- `MainWindowViewModel.Workspace.cs` owns workspace revisions, snapshot creation, and application of plugin edits alongside UI refresh work.
- `TranslationItem` primarily carries namespace/language addressing; source ownership is partly represented through format-specific metadata.
- `SaveContext` includes `NsTreeItems`, coupling persistence contracts to navigation.
- `ProjectLifecycleService.SaveProjectAsync` saves the project and sidecars, then marks all items saved. Async signatures currently wrap synchronous work.
- `SaveTransaction` protects listed files but also discovers newly created files by extension during rollback. Explicit save plans must replace that discovery to avoid removing unrelated concurrent creations.

These were the starting boundaries before extraction. The implementation status above describes which have changed; persistence and navigation contracts remain migration work.

## Plugin and framework lifecycle alignment

Use the existing plugin system rather than introducing a parallel activation manager. The relevant contracts are [PluginActivation](../../../Toucan.Plugins.Abstractions/Plugin/PluginActivation.cs), [PluginActivationService](../../../Toucan.Core/Plugins/PluginActivationService.cs), [IFrameworkProfile](../../../Toucan.Plugins.Abstractions/Contracts/IFrameworkProfile.cs), and [ProjectLifecycleService](../../../Toucan.Core/Services/ProjectLifecycleService.cs).

| Existing scope or contract | Engine integration |
|---|---|
| Plugin registration (`IToucanPlugin.Initialize`) | Register capabilities and factories only. Do not load workspace data, start watchers, or open connections. |
| Application activation | Application services and capability catalogs outlive all workspaces. Registered singleton framework profiles hold conventions, not mutable per-workspace state. |
| Workspace activation | One engine session per open workspace; workspace plugins can use its published snapshot and command API when activated. |
| Connection activation | Each source connection has its own adapter session, cancellation, resources, and subscriptions within its workspace. Use `OpenConnectionAsync`/`CloseConnectionAsync` for plugin connections. |
| Framework profile | Supplies discovery, language/package conventions, default format, and paths for new documents; the engine owns the resulting document bindings. |
| Navigator/presenter | Attaches to an existing workspace. Changing the view does not activate/deactivate workspace or connection plugins. |

`ProjectLifecycleService` remains the host's open/close/save coordinator and delegates state and persistence to the engine. `IPluginActivationService` remains responsible for plugin activation and disposal. Define one owner for every engine, adapter, watcher, and subscription; do not dispose the same adapter through both the engine and a plugin session.

### Open ordering

1. Resolve the configured framework and format from registered capabilities. An explicit setting wins over detection; detect only when unconfigured. Report missing explicit capabilities rather than silently changing routing or codecs.
2. Create a provisional engine session, discover and load local documents, and apply sidecars and resolved recovery state before exposing an authoritative baseline. A remote-only workspace can publish an explicit loading state before connection activation supplies its initial snapshot.
3. Attach the engine-backed workspace API and publish its initial state before calling `OpenWorkspaceAsync`. The existing `WorkspaceOpened` event is emitted before workspace activators run; preserve this meaning. Do not reinterpret it as a UI-ready event or await reentrant lifecycle calls inside its handlers.
4. Activate workspace plugins, then configured plugin connections. Connection initial data enters through engine commands with session/connection identity. Enable normal watcher/autosave operation once initialization is coherent, then notify presenters through the existing project-open integration.
5. Report individual activation failures through existing activation results/diagnostics. An optional failing plugin does not close the workspace; an unavailable required source remains explicitly unavailable and cannot be saved through another adapter.

Failed or cancelled opening must unwind any connections and workspace activation already started, detach the provisional backend, and dispose owned resources. Plugin activation must not depend on `MainWindowViewModel` receiving a later project-open event before snapshots become accessible.

### Close and switch ordering

Resolve pending editor text and the unsaved-changes decision before committing to close. Cancellation leaves the session and plugin scopes open. Once closing begins, stop scheduling autosaves/watch refreshes, reject new mutations, and settle or recover in-flight persistence before disposing its source resources.

Call `CloseWorkspaceAsync` while workspace state and read access still exist. Preserve its cancellation-before-disposal behavior and connection-before-workspace teardown; sessions are disposed in reverse activation order within each scope. Complete engine streams, detach the backend, and clear workspace state after plugin sessions end. Application sessions end last at host shutdown. Keep the existing `WorkspaceClosed` meaning: plugin sessions have ended; it is not a substitute for final engine disposal notification.

Switching the entire project follows close then open with a fresh engine session generation. Keep the existing workspace identifier used by plugin configuration/storage stable; use a separate generation ID to reject late work after reopen. Replacing a connection inside the same workspace closes only that connection scope and reconciles its owned dirty entries before removing bindings; it must not reactivate unrelated workspace plugins. Framework changes require a controlled rediscovery/rebind plan and pending-edit resolution, never a path change inferred from a presenter switch.

### Framework discovery and save routing

Use `DiscoverFiles` results (`AbsolutePath`, `RelativePath`, `Language`, `Package`) to create document bindings. Preserve package/namespace distinctions and nested relative paths. `ExtractLanguage` supports discovery; `DefaultFormatId` supplies a default when no explicit format is configured. Format strategies remain responsible for parsing and round-trip serialization.

Existing bindings are authoritative for updates and renames. Use `GetFilePath(root, language, package)` when creating a document and only when it selects a unique supported destination; do not regenerate all existing paths from defaults. For example, changing an i18next entry in `public/locales/en/checkout.json` must retain that exact discovered location even if a profile's default output path uses another locale directory. Unresolved package or layout ambiguity requires an explicit destination.

The existing profile contract cannot necessarily express every layout, per-document codec, or new-entry destination rule. Add compatible optional capabilities or a versioned contract where needed; do not silently reinterpret `Package` or break third-party profiles. Preserve existing plugin trust, capability checks, reserved IDs, and API compatibility checks when registering new source capabilities. This plan defines required integration work, not functionality already supplied by the profile interface.

Acceptance: plugin activation sees the correct engine state; close cancellation preserves scopes; connection replacement leaves unrelated plugins running; discovery and save routing agree for built-in and third-party profiles.

## 1. Document ownership and stable identity

Introduce `DocumentId`, stable `EntryId`, translation units identified by entry and language, and source bindings mapping persisted units to documents and locations. A document records source location, format, and saved version. Keep format-specific round-trip metadata separate from these identities.

Namespace and key paths retain structured segments. Do not assume every dot is a separator. The same logical path in different files remains distinct:

```text
checkout.json -> errors -> required
profile.json  -> errors -> required
```

Language variants may reside in different documents. The loader/layout contract must describe their relationship explicitly rather than joining units solely because their key text matches. Stable identity must survive rename; define reopen and recovery identity behavior before adopting it in journals or plugin contracts.

Acceptance: duplicate paths across documents survive load, edit, and save independently.

## 2. UI-independent workspace ownership

Move revisions, snapshots, command application, validation policy, dirty tracking, and undo coordination into Core. Reuse `WorkspaceEditPlanner` where practical. All mutations use engine commands: typing, bulk edits, plugin changes, approvals, rename, delete, moves, and undo/redo.

Presenters consume immutable state instead of mutating shared `TranslationItem` objects. View-model code remains responsible for UI selection, focus, and rendering. Pending editor text must enter the engine before switching sessions, saving, or taking an authoritative snapshot.

Preserve the existing plugin workspace API through a facade. Its key/language addressing must refuse ambiguous matches. Add an ID-based API for full multi-document support, with an explicit compatibility/versioning decision before changing published contracts.

Acceptance: a headless workspace loads, edits, validates, undoes changes, and produces snapshots without an Avalonia dispatcher.

## 3. Async commands and subscriptions

Phase 3 is implemented. Core `ApplyAsync` and `ApplyEntriesAsync` freeze their input and use a bounded FIFO command queue. Session/revision checks and transaction validation run under the workspace gate. Waiting commands cancel promptly, including when the queue is full. Cancellation during validation aborts before commit; cancellation after commit preserves the applied result. Closing cancels queued commands for that session. The desktop adapter serializes native/plugin edits on the UI thread, flushes pending text, and calls the same synchronous transaction implementation without blocking on the Core worker. Legacy synchronous change handlers can issue reentrant commands; these execute inline to avoid waiting on their own worker.

`IWorkspaceSubscriptionApi.SubscribeAsync(capacity, ct)` atomically captures an immutable identified snapshot and registers its continuation. Each subscription has one reader and a separate bounded channel (default 128, allowed 1–4096). Deltas carry workspace/session IDs, previous/current revisions, entry IDs and languages, replacement units or deletions, origin, language configuration, dirty state, and closing state. Save acknowledgments, reloads, undo/redo, and configuration changes also publish revisions. Consumers apply deltas to their initial snapshot and verify revision continuity; UI dispatch belongs to the consumer.

Overflow replaces buffered changes with a terminal `ResnapshotRequired` event and completes the stream. Consumers discard their local mirror and subscribe again. Other subscribers continue independently. `Closing` preserves readable state for plugin teardown; `Closed` ends the old session, including during direct replacement, Save As, and service disposal. If the close event cannot fit, it replaces buffered updates and must be treated as terminal rather than applied as a continuous delta. Explicit disposal, subscription-token cancellation, or ending enumeration detaches the reader. Plugin activation owners should pass their lifetime token or dispose the subscription during teardown. Source connection scopes remain a separate lifecycle integration task.

Acceptance coverage includes two independent consumers, reconstructing state from deltas, snapshot/commit races, overflow, cancellation before/during/after command execution, bounded queue waiting, legacy observer reentrancy, close/reopen isolation, service disposal, and the desktop plugin adapter.

## 4. Explicit save plans

Separate source access, layout/routing, and format serialization. Adapt existing format strategies incrementally. A plan declares the complete create/update/delete set, expected source versions, and the captured workspace revision. Include comments, audit sidecars, and manifests only when affected.

Saving may rewrite an affected document if its format requires it. Unrelated documents must remain byte-for-byte unchanged with unchanged modification times. A no-change save must not rewrite source documents.

Routing rules:

- Rename preserves source ownership.
- Cross-document moves affect both documents.
- Deletions retain their original binding until persisted.
- New entries require a destination document or an unambiguous layout rule.
- Save As and export use separate destination plans.

Execute saves from immutable captured state, keeping I/O outside the mutation queue where possible. Recheck source versions before writing and surface conflicts. A save of revision N acknowledges only captured state; edits arriving during I/O remain dirty, including edits to the same unit.

Keep recoverable multi-file persistence. Journal and roll back exactly the declared writes, including planned new and deleted files. Do not discover rollback targets by extension. Preserve drafts and dirty state on failure; report incomplete rollback accurately. Version recovery data if required by the new identity model.

Acceptance: a single-document edit never writes unrelated files; failed multi-file writes recover only planned targets; later edits remain dirty after an earlier save completes.

## 5. Navigation and presenter projections

Introduce navigation nodes referencing engine IDs. Remove `NsTreeItem` from new persistence contracts. Support:

```text
Namespace -> nested groups -> key
File -> namespace -> nested groups -> key
```

Selection is a query over entry IDs. The current editor panel consumes it; other presenters can consume the same selection independently. Grouped namespace views show file origins where paths collide. Switching navigator or presenter preserves edits and selection where possible and never changes source bindings.

Acceptance: both navigation layouts edit the same workspace and produce the same save targets.

## 6. External changes, switching, and migration

Route watcher notifications through source adapters. Reload clean documents and report conflicts for dirty documents. Distinguish self-generated writes by document/version rather than suppressing all watcher events during saves. Carry changed-document information through the watcher boundary.

Whole-workspace source switching resolves pending edits, closes the previous session, disposes its subscriptions, and opens a new session. Replacing one connection follows the narrower connection lifecycle above. Changing a presenter does not close the workspace. Source conversion or rebinding requires an explicit destination plan.

Migrate built-in formats incrementally. Legacy adapters declare their broader save scope and cannot advertise targeted-save support until verified. Preserve existing unsupported-construct guards, encoding/line-ending behavior, comments, approval policy, autosave, and crash recovery.

Acceptance: external changes and late old-session events cannot overwrite or contaminate the active workspace.

## Delivery and verification

Implement phases 1–4 for multi-file JSON first; add both navigators next; finish external-change handling and format migration after the core contracts hold. Track remaining work in the linked tracker, removing shipped tasks there rather than ticking them.

Regression fixtures must exercise duplicate keys across files, literal dots, language variants in separate files, rename/delete/move routing, destination ambiguity, no-change saves, edits during saves, external conflicts, failure recovery, plugin ambiguity, and subscriber overflow/cancellation. Include unrelated-file hashes and timestamps, and an unrelated file created while a save fails.

This work elaborates the roadmap's content-source and synchronization milestones. It does not change their release assignments or claim that implementation has shipped.
