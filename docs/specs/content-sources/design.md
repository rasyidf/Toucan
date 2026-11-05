---
title: "content-sources — design"
status: planned
progress: "0/9 phases"
updated: 2026-11-05
summary: "Full design for content-source adapters: plugin API 1.3 contracts, host-owned accounts, connections and auth, a Core sync engine with baselines, journals and three-way plans, desktop Sources UI, CLI, and the reference and first online connectors."
---
# Design: content sources

Requirements: [requirements.md](requirements.md). Work order: [tasks.md](tasks.md). Roadmap milestones: [v0.27, v0.28 and v0.29](../../todos/future-roadmap.md#v027-content-sources-and-snapshot-mode).

## 1. Summary

A content source is anything outside the open project that holds translations Toucan should read from or write to: a translation management system (Crowdin, Lokalise, Phrase, Transifex), a company's own string service, or a file store (a Git host, S3, WebDAV). Today a plugin can reach one only by building everything itself, as the sample connector does: login, remote identity, pull/push, conflict handling and status.

This design splits that work along one rule: **the adapter translates, the host decides.**

- An **adapter** (a plugin) knows one service's API: how to authenticate, list projects and resources, read units or documents, and write changes. It holds no sync state and makes no merge decisions.
- The **host** owns accounts, credentials, connections, mapping, the sync baseline, the pending-operation journal, three-way comparison, plans, conflicts, retry, recovery and every piece of UI and CLI.

Every connector then gets the same safety guarantees, the same UI and the same CLI for free, and a connector is small enough that third parties can write and maintain one.

```text
                    ┌────────────── Host (Toucan.Core + desktop + CLI) ──────────────┐
 Sources UI / CLI ──┤ Accounts · Connections · Auth broker · Source HTTP client      │
                    │ Sync engine: baseline · journal · three-way compare · plans    │
                    │ WorkspaceService (edits, undo, review, save)                    │
                    └───────────────┬───────────────────────────┬────────────────────┘
                                    │ IContentSourceSession      │ format strategies
                        ┌───────────┴───────────┐     ┌─────────┴─────────┐
                        │ Unit adapter          │     │ Document adapter  │
                        │ (key/value REST API)  │     │ (files + ETags)   │
                        └───────────────────────┘     └───────────────────┘
```

## 2. What exists and what this builds on

Verified against the code on 2026-11-05.

| Existing piece | Where | Role in this design |
|---|---|---|
| Plugin registration/activation split, `PluginLifetime.Connection` | `Toucan.Plugins.Abstractions/Plugin/PluginActivation.cs` | Adapter sessions open in a connection scope. |
| `OpenConnectionAsync` / `CloseConnectionAsync` | `Toucan.Core/Plugins/PluginActivationService.cs` | Host calls these per connection. Today they are only called from tests, and `RunAsync` activates **every** plugin's connection activators, not the owning plugin's. Phase 0 fixes this. |
| Config schema with `ConfigScope.Connection`, `ConfigFieldType.Secret/Url` | `Plugin/Services.cs` | Adapter settings forms and per-connection values. |
| `IPluginSecrets`, `SecretService` (file of values encrypted through `ISecureStorageService`) | `Toucan.Core/Services/SecretService.cs` | Credential storage, behind a new broker. |
| `IPluginDiagnostics.Redact/RegisterSecret` | `Plugin/Services.cs` | Redaction of tokens in logs and errors. |
| `IBackgroundOperations` | `Plugin/Services.cs` | Progress and cancellation for pull/push. |
| `WorkspaceService` commands, revisions, undo, `WorkspaceChangeOrigin.Source` | `Toucan.Core/Services/WorkspaceService*.cs`, `Plugin/WorkspaceSubscription.cs` | Pull applies as normal engine transactions with origin `Source`. |
| Stable `EntryId`, `DocumentId`, identity catalog | workspace sync engine phase 1 | Remote IDs map to entry IDs, not key strings. |
| `IWorkspaceSourceReader`, `IWorkspaceDocumentStore`, `WorkspaceDocumentSavePlanner` | `Toucan.Core/Contracts/Services/`, `Toucan.Core/Services/` | Document adapters plug into the same planner via a remote document store. |
| `ReviewStatus { Draft, NeedsReview, Approved }`, `ReviewTracker` | `Toucan.Core/Models/ReviewStatus.cs` | Remote statuses map onto these. The plugin-facing `ReviewState` still has only `Draft` and `Approved`. |
| Sample connector `IRemoteSource` | `samples/Toucan.Sample.Connector/RemoteSource.cs` | Replaced by the public contract; the sample is ported in phase 6. |
| `Toucan.Plugins.Testing` (`PluginTestHost`, `InMemoryWorkspace`) | `Toucan.Plugins.Testing/` | Gains a fake source server and an adapter conformance suite. |

## 3. Concepts and vocabulary

| Term | Meaning |
|---|---|
| **Adapter** | A registered implementation of `IContentSourceAdapter` for one service type, e.g. `acme.lokalise`. Stateless; creates sessions. |
| **Account** | Who the user is on a service instance: adapter ID, base URL, auth method, credential reference, display name. User-level, reusable across projects, never stored in a project. |
| **Connection** | A binding from one workspace to one remote project through one account: remote project ID, resource and language mapping, role, sync policy. Shared through the project file (minus credentials). |
| **Session** | A live `IContentSourceSession` for one connection, opened in the connection scope, disposed when the connection closes. |
| **Remote unit** | One translation as the service holds it: resource, remote key, language, value, optional revision, status, comment, metadata. |
| **Remote document** | One file as a file-based service holds it: path, bytes, version (ETag or hash). |
| **Baseline** | Per connection: the last state both sides agreed on, per unit. The common ancestor for three-way comparison. |
| **Journal** | Per connection: outbound operations not yet confirmed by the service, with idempotency keys. Survives restart. |
| **Plan** | A computed, immutable list of pull and push operations and conflicts for one connection, shown before anything changes. |
| **Role** | `Mirror`: local files are primary, the service is a sync target (developer repo plus TMS). `Primary`: the service is primary and Toucan keeps a local cache (translator without a repo). |

## 4. Plugin API 1.3 contracts

New public types live in `Toucan.Plugins.Abstractions/Sources/`, namespace `Toucan.Plugins.Sources`. `PluginApi.Current` becomes 1.3. Everything is additive: 1.0–1.2 plugins load unchanged. A plugin that registers an adapter declares `apiVersion: "1.3"` and the new `sources` capability.

The shape follows existing patterns: a required core, optional capabilities as separate interfaces discovered with `is`, and a capability record the host can show and check without calling the adapter.

### 4.1 Registration

```csharp
public interface IPluginContext
{
    // existing members ...

    /// <summary>Adds a content-source adapter. Needs the <c>sources</c> capability. The adapter ID must start with the plugin ID and a dot.</summary>
    void AddSourceAdapter(IContentSourceAdapter adapter);
}
```

`IPluginContext` is implemented only by the host and `Toucan.Plugins.Testing`, so adding a member is safe. Registration validation mirrors `AddFormat`: unique ID, prefix rule, reserved built-in IDs, descriptor validated (non-empty name, at least one auth method, allowed hosts well-formed). Registration does no I/O.

The manifest gains an optional `network` block, shown in the trust prompt:

```json
{
  "capabilities": ["sources", "commands"],
  "network": { "hosts": ["api.lokalise.com", "*.lokalise.com"], "userDefinedHosts": false }
}
```

`userDefinedHosts: true` lets an account supply its own base URL (self-hosted servers, the custom REST adapter). The host HTTP client refuses requests outside declared hosts plus the account's base URL host (section 6.4).

### 4.2 Adapter and descriptor

```csharp
public interface IContentSourceAdapter
{
    SourceAdapterDescriptor Descriptor { get; }

    /// <summary>Opens a session for one connection. Called inside the connection scope. May do network I/O.</summary>
    Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken);
}

public sealed record SourceAdapterDescriptor
{
    public required string Id { get; init; }                     // "acme.lokalise"
    public required string DisplayName { get; init; }
    public string Icon { get; init; } = "Cloud";
    public Uri? DefaultBaseUrl { get; init; }
    public bool AllowsCustomBaseUrl { get; init; }
    public required IReadOnlyList<SourceAuthMethod> AuthMethods { get; init; }
    public required SourceCapabilities Capabilities { get; init; }
    public SourceValueModel ValueModel { get; init; } = SourceValueModel.PlainText;
    public ConfigSchema? ConnectionSettings { get; init; }      // extra non-secret fields, e.g. branch name
    public int ContractVersion { get; init; } = 1;
}

[Flags]
public enum SourceCapabilities
{
    None = 0,
    ReadUnits = 1 << 0,          // IUnitSourceSession
    WriteValues = 1 << 1,
    CreateKeys = 1 << 2,
    RenameKeys = 1 << 3,
    DeleteKeys = 1 << 4,
    DeleteTranslations = 1 << 5,
    ConditionalWrites = 1 << 6,  // per-unit revision or ETag checks
    ReadDocuments = 1 << 7,      // IDocumentSourceSession
    WriteDocuments = 1 << 8,
    DeltaReads = 1 << 9,         // ISupportsDeltaReads
    ReviewStatus = 1 << 10,
    Comments = 1 << 11,
    UnitMetadata = 1 << 12,      // descriptions, max length, tags, screenshots
    AsyncJobs = 1 << 13,         // writes complete later
    ChangeFeed = 1 << 14,        // ISupportsChangeFeed (post-1.0 consumer)
    ProjectDiscovery = 1 << 15,  // ISupportsProjectDiscovery
    Plurals = 1 << 16,
}

public enum SourceValueModel { PlainText, Icu, Platform }
```

The host checks every plan against `Capabilities` before execution and refuses a plan that needs one the adapter lacks, so an adapter never receives an operation it did not declare. Capabilities can be narrowed per connection at runtime by `SourceProjectInfo.Permissions` (a read-only API token on a writable service).

### 4.3 Session context

```csharp
public interface ISourceSessionContext
{
    string WorkspaceId { get; }
    string ConnectionId { get; }
    SourceConnectionInfo Connection { get; }      // remote project ID, base URL, mapping, settings (no secrets)
    ISourceCredentials Credentials { get; }       // section 6
    ISourceHttpClientFactory Http { get; }        // section 6.4
    IPluginDiagnostics Diagnostics { get; }
    ILogger Logger { get; }
    CancellationToken Scope { get; }              // the connection scope
    TimeProvider Time { get; }                    // testable clocks for backoff and expiry
}
```

### 4.4 Session: required core

```csharp
public interface IContentSourceSession : IAsyncDisposable
{
    /// <summary>Cheap check that credentials work and the project is reachable. Used by "Test connection".</summary>
    Task<SourceTestResult> TestAsync(CancellationToken cancellationToken);

    /// <summary>Remote project facts: name, languages, resources, permissions, limits.</summary>
    Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken);
}

public sealed record SourceProjectInfo(string RemoteProjectId, string Name, string? SourceLanguage,
    IReadOnlyList<RemoteLanguage> Languages, IReadOnlyList<RemoteResource> Resources,
    SourceCapabilities Permissions, SourceLimits Limits);

public sealed record RemoteLanguage(string Code, string? DisplayName, bool IsSource);
public sealed record RemoteResource(string Id, string Name, string? Path, string? FormatHint);
public sealed record SourceLimits(int? MaxBatchSize = null, int? MaxConcurrency = null, int? RequestsPerMinute = null);
```

A session implements at least one of the two data interfaces below.

### 4.5 Unit sessions (key/value APIs)

```csharp
public interface IUnitSourceSession : IContentSourceSession
{
    /// <summary>Reads pages until done. The final page sets IsComplete; a read that ends without it is treated as partial.</summary>
    IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, CancellationToken cancellationToken);

    /// <summary>Applies a batch. Returns one result per operation, in order. Never throws for a refused operation.</summary>
    Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken);
}

public sealed record UnitReadRequest(IReadOnlyList<string>? ResourceIds, IReadOnlyList<string>? Languages, string? ContinuationToken = null);
public sealed record RemoteUnitPage(IReadOnlyList<RemoteUnit> Units, string? ContinuationToken, bool IsComplete, string? Cursor = null);

public sealed record RemoteUnit(RemoteKey Key, string Language, string Value)
{
    public string? Revision { get; init; }                  // opaque, compared for equality only
    public RemoteReviewStatus? Status { get; init; }
    public string? Comment { get; init; }
    public IReadOnlyDictionary<string, string>? PluralForms { get; init; }
    public SourceUnitMetadata? Metadata { get; init; }
}

/// <summary>Remote identity. RemoteId is the service's stable ID when it has one; Name is the key text.</summary>
public sealed record RemoteKey(string ResourceId, string Name, string? RemoteId = null);

public sealed record RemoteReviewStatus(string RemoteValue, SourceReviewMeaning Meaning);
public enum SourceReviewMeaning { Untranslated, Draft, NeedsReview, Reviewed, Approved, Rejected, Unknown }

public sealed record SourceUnitMetadata(string? Description = null, int? MaxLength = null,
    IReadOnlyList<string>? Tags = null, IReadOnlyList<Uri>? Screenshots = null,
    IReadOnlyDictionary<string, string>? Extra = null);
```

Write operations carry an idempotency key generated by the host's journal, and the expected revision when the adapter declares `ConditionalWrites`. Their names avoid the existing `RenameKey`, `DeleteKey` and `AddKey` workspace edits:

```csharp
public abstract record UnitWriteOperation(Guid OperationId);   // OperationId is the idempotency key
public sealed record PutValue(Guid OperationId, RemoteKey Key, string Language, string Value, string? ExpectedRevision) : UnitWriteOperation(OperationId);
public sealed record PutStatus(Guid OperationId, RemoteKey Key, string Language, SourceReviewMeaning Meaning, string? ExpectedRevision) : UnitWriteOperation(OperationId);
public sealed record PutComment(Guid OperationId, RemoteKey Key, string Language, string Comment) : UnitWriteOperation(OperationId);
public sealed record CreateRemoteKey(Guid OperationId, RemoteKey Key, IReadOnlyDictionary<string, string> Values, SourceUnitMetadata? Metadata) : UnitWriteOperation(OperationId);
public sealed record RenameRemoteKey(Guid OperationId, RemoteKey Key, string NewName) : UnitWriteOperation(OperationId);
public sealed record DeleteRemoteKey(Guid OperationId, RemoteKey Key) : UnitWriteOperation(OperationId);
public sealed record DeleteTranslation(Guid OperationId, RemoteKey Key, string Language, string? ExpectedRevision) : UnitWriteOperation(OperationId);

public sealed record UnitWriteBatch(IReadOnlyList<UnitWriteOperation> Operations);
public sealed record UnitWriteResult(IReadOnlyList<UnitOperationResult> Results, SourceJobHandle? Job = null);

public sealed record UnitOperationResult(Guid OperationId, UnitOperationOutcome Outcome)
{
    public string? NewRevision { get; init; }
    public string? AssignedRemoteId { get; init; }   // for CreateRemoteKey
    public RemoteUnit? CurrentRemote { get; init; }  // on Conflict, when the service returns it
    public SourceError? Error { get; init; }
}

public enum UnitOperationOutcome { Applied, AlreadyApplied, Conflict, NotFound, Refused, RateLimited, Failed, Unknown }
```

`Unknown` is the honest answer when the request may or may not have reached the service (timeout after send, connection reset). The host then reconciles by reading before retrying (section 7.6). `AlreadyApplied` lets an adapter that recognizes a repeated idempotency key say so.

### 4.6 Document sessions (file-based services)

Many services exchange whole files: Crowdin and Transifex file resources, Git hosting APIs, S3, WebDAV. For them the adapter only moves bytes, and the host parses and serializes with the existing format strategies.

```csharp
public interface IDocumentSourceSession : IContentSourceSession
{
    IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, CancellationToken cancellationToken);
    Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken);

    /// <summary>Conditional write. ExpectedVersion null means "must not exist". Returns the new version.</summary>
    Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken);
    Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken);
}

public sealed record RemoteDocumentRef(string Path, string? Language, string? ResourceId);
public sealed record RemoteDocumentPage(IReadOnlyList<RemoteDocumentInfo> Documents, string? ContinuationToken, bool IsComplete);
public sealed record RemoteDocumentInfo(RemoteDocumentRef Ref, string Version, long? Length);
public sealed record RemoteDocumentContent(RemoteDocumentRef Ref, byte[] Bytes, string Version);
public sealed record RemoteDocumentWrite(Guid OperationId, RemoteDocumentRef Ref, byte[] Bytes, string? ExpectedVersion, string? FormatId);
public sealed record DocumentWriteResult(UnitOperationOutcome Outcome, string? NewVersion, SourceError? Error = null, SourceJobHandle? Job = null);
```

Core wraps a document session in `RemoteWorkspaceDocumentStore : IWorkspaceDocumentStore` and `RemoteWorkspaceSourceReader : IWorkspaceSourceReader`. The existing `WorkspaceDocumentSavePlanner` and format strategies then produce byte-exact, minimal-diff documents for a remote store exactly as they do for a local folder. A service that converts files server-side (Crowdin exports) declares which format IDs round-trip; others are refused at connection setup.

The **local-folder adapter** (roadmap v0.27) is the document session over the existing local reader and store. It ships as a built-in module and proves the boundary: existing projects open through it without fidelity changes.

### 4.7 Optional capabilities

| Interface | Capability flag | Purpose |
|---|---|---|
| `ISupportsProjectDiscovery` | `ProjectDiscovery` | `ListProjectsAsync(account)` for the project picker during setup. Called with an account-scoped context before a connection exists. |
| `ISupportsDeltaReads` | `DeltaReads` | `ReadChangesAsync(cursor)` returning changed and deleted units since a cursor. The host still runs a periodic full read to detect drift and never infers deletion from a delta alone unless the adapter marks it authoritative. |
| `ISupportsAsyncJobs` | `AsyncJobs` | `GetJobAsync(handle)` for writes or exports that finish later. Job handles are journaled so a restart resumes polling. |
| `ISupportsChangeFeed` | `ChangeFeed` | `WatchAsync(cursor)` yielding change hints (webhook relay, long poll, SSE). Hints trigger a delta read; they are never applied directly. Consumed by post-1.0 background sync; the contract ships now so adapters can implement it once. |
| `ISupportsUnitContext` | `UnitMetadata` | Lazily fetch heavy context (screenshots) for the inspector. |
| `ISourceAuthExchange` | (auth method `Custom`) | Section 6.3. |

New optional interfaces can be added in later minor versions without breaking adapters; the descriptor's `ContractVersion` lets the host disable features an older adapter does not understand.

### 4.8 Errors

```csharp
public sealed record SourceError(SourceErrorKind Kind, string Message)
{
    public TimeSpan? RetryAfter { get; init; }
    public string? RemoteCode { get; init; }
    public string? HelpUrl { get; init; }
}

public enum SourceErrorKind { Authentication, Authorization, NotFound, Conflict, Validation, RateLimited, Unavailable, Network, Timeout, Unsupported, Internal }
```

Adapters return `SourceError` in results and may throw `SourceException(SourceError)` from reads. The host maps kinds to actions: `Authentication` triggers re-login, `Authorization` shows the missing permission, `RateLimited` and `Unavailable` back off, `Validation` fails the operation without retry. Messages pass through `Redact` before display.

## 5. Accounts and connections (host-owned)

### 5.1 Two records, two homes

Splitting account from connection lets one login serve many projects, lets teammates share a connection binding through the project file while each signs in as themselves, and keeps credentials out of every file that might be committed.

**Account** (user-level, `accounts.json` in the application-data folder; credentials in the secret store):

```json
{ "id": "acc_7f3c", "adapter": "acme.lokalise", "baseUrl": "https://api.lokalise.com", "auth": "oauth-device",
  "displayName": "Lokalise (rasyid@work)", "credentialRef": "sources/acc_7f3c" }
```

**Connection** (in `toucan.tproj`, new `sources` array; schema bump with migration):

```json
"sources": [{
  "id": "con_1a2b",
  "adapter": "acme.lokalise",
  "baseUrl": "https://api.lokalise.com",
  "remoteProject": { "id": "4821.abc", "name": "Checkout app" },
  "role": "mirror",
  "languages": { "zh-Hans": "zh_CN", "pt-BR": "pt_BR" },
  "resources": [{ "remote": "web/checkout.json", "package": "checkout" }],
  "keys": { "separator": ".", "caseSensitive": true },
  "policy": { "pushStatuses": true, "pushComments": false, "deleteRemote": "ask" },
  "settings": { "branch": "main" }
}]
```

On open, the host resolves each connection to a local account by adapter and base URL. None found shows "Sign in to connect" in the Sources area; more than one asks once and remembers the choice per workspace in local state. A connection without a resolvable account stays visible and editable offline but cannot pull or push.

### 5.2 Lifecycle

Order extends the [workspace sync engine open ordering](../workspace-sync-engine/design.md#open-ordering):

1. Workspace opens; `WorkspaceService` loads local content, baselines and journals for each connection.
2. Workspace activators run.
3. For each connection with a resolved account, the host calls `OpenConnectionAsync(workspaceId, connectionId, owningPluginId)`. Only that adapter's plugin activates (phase 0 fix). The host then calls `adapter.OpenSessionAsync` inside that scope. Failure marks only that connection unavailable.
4. Sessions open lazily by default: the host defers step 3 until the first pull, push, test or scheduled check, so opening a project never blocks on the network. `policy.connectOnOpen: true` opts in.
5. Close: cancel in-flight sync operations (the journal records their state), dispose sessions, then `CloseConnectionAsync`, before workspace teardown. Removing or re-pointing a connection closes only its scope.

### 5.3 Mapping

- **Languages**: explicit project-to-remote code map, prefilled by matching BCP 47 tags with normalization (`zh-Hans` ↔ `zh_CN` suggested, never silent). Unmapped remote languages are ignored and listed; unmapped project languages are not pushed.
- **Resources**: remote resource to project package or document family. Unit adapters with a single flat namespace map to one package. Document adapters map paths with the framework layout (`DescribeDestination`) so a pulled new language lands where reopen discovers it.
- **Keys**: remote key name to namespace and key path through a separator and escaping rule. Collisions (two remote keys mapping to one entry) are refused at setup with a list. After the first sync, identity is the stored `RemoteKey.RemoteId` (when present) bound to `EntryId`, so local and remote renames are detected as renames, not delete plus create.
- **Values**: `SourceValueModel` tells validation what syntax to expect. Placeholder conversion between dialects (`%s` vs `{0}`) is a mapping option implemented in the host, not per adapter, and off by default.

## 6. Authentication and network

### 6.1 Auth methods

```csharp
public abstract record SourceAuthMethod(string Id, string Label);
public sealed record ApiTokenAuth(string Id, string Label, string? HeaderName = "Authorization", string? Scheme = "Bearer", Uri? TokenHelpUrl = null) : SourceAuthMethod(Id, Label);
public sealed record BasicAuth(string Id, string Label) : SourceAuthMethod(Id, Label);
public sealed record OAuthAuthorizationCodeAuth(string Id, string Label, Uri AuthorizeUrl, Uri TokenUrl, string ClientId,
    IReadOnlyList<string> Scopes, bool UsePkce = true, Uri? RevokeUrl = null) : SourceAuthMethod(Id, Label);
public sealed record OAuthDeviceCodeAuth(string Id, string Label, Uri DeviceAuthorizationUrl, Uri TokenUrl, string ClientId,
    IReadOnlyList<string> Scopes) : SourceAuthMethod(Id, Label);
public sealed record CustomAuth(string Id, string Label, ConfigSchema Fields) : SourceAuthMethod(Id, Label);
```

The host runs every flow. The desktop app runs authorization code with PKCE through a loopback redirect (`http://127.0.0.1:{random port}/callback`) in the system browser, with state and PKCE verification; device code works on desktop and CLI. No embedded web views: they expose credentials to the host process and many providers block them. Client secrets are not supported in descriptors because a distributed plugin cannot keep one; services that require one need a custom exchange or a token.

### 6.2 Credential broker

```csharp
public interface ISourceCredentials
{
    /// <summary>A valid credential, refreshed if close to expiry. Throws SourceException(Authentication) when the user must sign in again.</summary>
    Task<SourceCredential> GetAsync(CancellationToken cancellationToken);

    /// <summary>Called after a 401 the adapter believes is an expired token. Refreshes once; concurrent callers share the refresh.</summary>
    Task<SourceCredential> RefreshAsync(SourceCredential rejected, CancellationToken cancellationToken);
}

public sealed record SourceCredential(string Scheme, string Value, DateTimeOffset? ExpiresAt);
```

The broker (Core, `SourceCredentialBroker`) stores access and refresh tokens under the account's `credentialRef` in `ISecretService`, registers every value with diagnostics redaction, serializes refreshes per account across the desktop app and CLI (file lock next to `secrets.json`), and revokes tokens on sign-out when the method has a revoke URL. Adapters never see refresh tokens.

Secret storage moves from the current encrypted file to the OS store where available (macOS Keychain, Windows Credential Manager/DPAPI, libsecret on Linux), keeping the encrypted file as the fallback and for headless Linux. This is a host improvement behind `ISecretService` and benefits existing plugin secrets too.

### 6.3 Custom exchange

For services with username and password or a proprietary login, the adapter implements:

```csharp
public interface ISourceAuthExchange
{
    /// <summary>Turns user-entered fields into a credential. Fields marked Secret are passed once and never stored by the host.</summary>
    Task<SourceCredential> ExchangeAsync(string methodId, IReadOnlyDictionary<string, string> fields, ISourceHttpClientFactory http, CancellationToken cancellationToken);
    Task<SourceCredential> RefreshAsync(string methodId, SourceCredential current, ISourceHttpClientFactory http, CancellationToken cancellationToken) => throw new NotSupportedException();
}
```

The host renders the form from `CustomAuth.Fields`, calls the exchange, and stores only the returned credential. A desktop plugin can still contribute its own dialog through the existing desktop contribution package when a form is not enough; it must hand the result to the broker rather than storing it.

### 6.4 Source HTTP client

`ISourceHttpClientFactory.Create()` returns an `HttpClient` whose handler pipeline the host owns:

1. **Host allow-list**: refuses any request whose host is not in the manifest's `network.hosts` or the account's base URL host. HTTPS required except loopback.
2. **Auth**: attaches the current credential; on 401 refreshes once and retries idempotent requests.
3. **Rate limiting**: honors `Retry-After` and `X-RateLimit-*`, with a per-account token bucket seeded from `SourceLimits`.
4. **Retry**: exponential backoff with jitter for idempotent methods and for requests carrying an idempotency key; never for others.
5. **Timeouts and cancellation** linked to the connection scope.
6. **Redaction**: request and response logging off by default; when diagnostics are enabled, headers and bodies pass through `Redact`.
7. **Proxy and user agent** from app settings (`Toucan/{version} {pluginId}/{pluginVersion}`).

Adapters are not forced to use it (plugins are not sandboxed), but the conformance suite fails an adapter that makes requests outside it, and the trust prompt states that a plugin with `sources` can reach the listed hosts with the user's credentials.

## 7. Sync engine (Core)

New services under `Toucan.Core/Services/Sources/`: `SourceConnectionService` (connection registry and lifecycle), `SyncStateStore` (baseline and journal persistence), `SyncPlanner` (pure three-way comparison), `SyncExecutor` (applies plans, drives the adapter), `SyncCoordinator` (one entry point per connection, serializes runs, publishes status).

### 7.1 State on disk

Sync state is per user and per machine, so it is never committed. It lives in `.toucan/local/sources/{connectionId}/` and Toucan writes `.toucan/local/.gitignore` containing `*` when it creates the folder.

- `baseline.json` (schema-versioned): per bound unit `{ entryId, language, remoteKey, remoteRevision, valueHash, value, status, commentHash, syncedAt }`, plus per-document `{ path, version }` for document adapters, the last full-read time and the delta cursor.
- `journal.json`: outbound operations `{ operationId, kind, target, payload, expectedRevision, state, attempts, lastError, jobHandle }` with states `Planned → InFlight → (Confirmed | Failed | Unknown)`. Written through temp-and-rename before each send and after each result.
- `cache/` (role `primary` only): the last pulled remote content, so the workspace opens offline.

Baseline writes go through the same atomic write helper as local saves. A corrupt or newer-schema baseline is kept aside and the connection falls back to "first sync" mode (section 7.4), never to silent overwrite.

### 7.2 Comparison model

For each `(entry, language)` bound to a connection, the planner compares three values per field (value, review status, comment, existence, key path):

| Base → Local | Base → Remote | Result |
|---|---|---|
| unchanged | unchanged | nothing |
| changed | unchanged | push |
| unchanged | changed | pull |
| changed to X | changed to X | converge (update baseline only) |
| changed to X | changed to Y | conflict |
| deleted | changed | conflict (edit vs delete) |
| changed | deleted | conflict |
| deleted | unchanged | push delete, only if `policy.deleteRemote` allows; otherwise listed as "local deletion not pushed" |
| unchanged | deleted | pull delete, only if the read was complete; always requires explicit inclusion in the plan |
| absent | present | new remote unit → pull create (destination from mapping) |
| present | absent | new local unit → push create (if `CreateKeys`) |

Fields compare independently: a remote status change and a local value change on the same unit are not a conflict. Review meaning is reconciled through `ReviewTracker` rules: a pulled value change withdraws a local approval the same way an external file change does; a pushed approval is sent only when the local status is `Approved` against the current source fingerprint.

Unpaired renames are matched by `RemoteId` first, then by baseline entry ID, then reported as delete plus create the user can pair manually in the plan.

### 7.3 Plans

```csharp
public sealed record SyncPlan(string ConnectionId, Guid PlanId, long WorkspaceRevision, DateTimeOffset RemoteReadAt, bool RemoteReadComplete,
    IReadOnlyList<PullChange> Pull, IReadOnlyList<PushChange> Push, IReadOnlyList<SyncConflict> Conflicts,
    IReadOnlyList<SyncNotice> Notices);
```

A plan is immutable and built from a captured workspace snapshot and a completed remote read. Operations needing a capability the connection lacks become notices ("rename not supported by this service; will push as new key" is not allowed silently, it is a notice and the change is held). The user can exclude any change and must resolve or defer every conflict before the plan applies; deferred conflicts stay in the conflict list and block neither side's other changes.

Plan kinds: **Pull** (pull changes and conflicts only), **Push** (push changes; refuses if the remote moved since the last read and conditional writes are unavailable, forcing a pull first), **Sync** (both). The CLI and the desktop use the same planner.

### 7.4 First sync

With no baseline, there is no common ancestor. The planner treats equal values as converged, and every difference as a conflict with a bulk choice: "Use remote for all", "Use local for all", or review individually. Remote deletions and local deletions are not inferred. This is the most common place for data loss in connectors, so it is never a one-click default.

### 7.5 Applying a pull

Pull changes go through `WorkspaceService` as one transaction with origin `Source`, `OriginId = connectionId`, label `Pulled from {connection}` and `ExpectedValue` per unit equal to the snapshot value. Undo restores them like any edit. Units the user edited after the plan's snapshot are refused and re-planned, not overwritten. After the transaction commits, the baseline advances for exactly the applied units. Local saving stays separate: the pull marks the workspace dirty and normal save or autosave persists files. In role `primary` the cache updates as well.

### 7.6 Executing a push

1. Write all operations to the journal as `Planned` with fresh operation IDs.
2. Send in batches up to `MaxBatchSize` with bounded concurrency; mark `InFlight` before each send.
3. Per result: `Applied`/`AlreadyApplied` → advance baseline for that unit with `NewRevision`, mark `Confirmed`. `Conflict` → move to conflicts with `CurrentRemote`. `Refused`/`Failed` with a non-retryable kind → `Failed`, unit stays pending. `RateLimited` → back off and resend. `Unknown` → mark `Unknown`.
4. Before any retry of `Unknown`, read the affected units. If the remote now equals the intended value, confirm; if it equals the base, resend with the same operation ID; otherwise it is a conflict.
5. On restart, `InFlight` and `Unknown` entries go through step 4 before anything new is sent. Async job handles resume polling.
6. Partial success is reported per unit; nothing is rolled back remotely, because remote services have no transactions. The UI says this plainly.

Pushes never depend on local files being saved. The unit's delivery state is computed from the journal and baseline: `Pending`, `Sending`, `Synced`, `Failed`, `Conflict`. This is the "track delivery separately from review" item in the v0.26 roadmap.

### 7.7 Concurrency and scheduling

One run per connection at a time; requests for another run while one is active coalesce. Different connections run independently. Runs execute as `IBackgroundOperations` with progress and cancel. Cancellation between batches is clean; cancellation mid-batch leaves `InFlight` entries for reconciliation.

Manual pull and push ship first. A scheduled "check for remote changes" (read and plan only, no apply) is opt-in per connection and shows a badge. Automatic apply and change-feed driven sync are post-1.0 and reuse the same planner, so they inherit every guarantee above.

### 7.8 Multiple connections

A workspace can have several connections (a TMS for translators plus a CDN bucket for delivery). Each has its own baseline and journal. A unit may be bound to more than one connection only when at most one has role `primary`. Pulls from different connections that touch the same unit in the same run are planned sequentially, so the second sees the first's result as local state.

### 7.9 Workspace API exposure

Plugins read sync state through an optional `ISourceSyncApi` on `IPluginServices` (connections, status, delivery state per unit, last plan summary) and can request a plan or run. They cannot write baselines or journals. `ReviewState` in the plugin API gains `NeedsReview` in 1.3, matching Core's `ReviewStatus`.

## 8. Desktop experience

All built in the host; adapters contribute nothing unless they want an extra settings page.

- **Sources area**: a left side panel ("Sources") and a Settings page. Lists connections for the project with state (Not signed in, Connected, Offline, Syncing, Conflicts n, Failed), last successful pull and push, pending count. Actions: Pull, Push, Sync, Test connection, Edit, Remove.
- **Accounts page** in Settings: add, sign in again, sign out, see which projects use each account.
- **Add connection wizard**: choose adapter → choose or create account (runs the auth flow) → pick remote project (`ISupportsProjectDiscovery`, else type an ID) → map languages and resources with suggestions and collision checks → choose role and policy → Test connection → optional first pull with the first-sync review.
- **Plan preview dialog**: grouped by pull, push, conflicts and notices; per-row include toggle; counts; "Apply". Large plans virtualize.
- **Conflict view**: base, local and remote columns per field with Take local, Take remote, Edit merged, Defer. Keyboard accessible and reachable from the row badge.
- **Row and status bar indicators**: per-row delivery badge (Pending, Failed, Conflict) next to the existing review badge; status bar shows "Saved locally" and "Synced" separately.
- **Commands**: built-in `sources.pull`, `sources.push`, `sources.sync`, `sources.showConflicts`, each taking the active connection, in the palette and menus with reassignable shortcuts.
- **Notifications**: sign-in expired (sticky, with Sign in action), push partially failed, remote changes available.

## 9. CLI

```text
toucan sources list                          # connections in the project and their state
toucan sources accounts [add|remove|list]
toucan sources login <connection|account>    # device code, or --token-stdin
toucan sources test <connection>
toucan sources status <connection> [--json]
toucan sources pull <connection> [--plan] [--yes] [--take remote|local] [--json]
toucan sources push <connection> [--plan] [--yes] [--json]
toucan sources sync <connection> [--plan] [--yes] [--json]
```

`--plan` prints the plan and exits. Without `--yes`, an interactive terminal confirms; a non-interactive one refuses to apply. Conflicts never auto-resolve unless `--take` is given. CI credentials come from `TOUCAN_SOURCE_TOKEN_<CONNECTIONID>` or `--token-stdin` and are never written to disk. Exit codes: 0 done, 2 conflicts present, 3 authentication required, 4 partial failure, 1 other errors. Pull saves files through the existing document coordinator after applying.

## 10. Security

- Credentials live only in the secret store; project files hold connection bindings, logs and diagnostics hold redacted text.
- The trust prompt lists `sources`, declared network hosts and whether the plugin accepts user-defined hosts. Changing the `network` block changes the hash, so trust is asked again.
- The source HTTP client enforces the host allow-list and HTTPS.
- OAuth uses PKCE, state and loopback redirect with a random port; device code shows the verification URL from the provider's response only after it is checked against the method's declared host.
- Tokens are scoped to their account; one account's credentials are never passed to a session for another account.
- Plugins are still unsandboxed. Signed plugins and a commercial channel arrive with v0.29 ([roadmap](../../todos/future-roadmap.md#v029-first-online-connector)); the spec does not claim isolation it does not have.

## 11. Testing strategy

- **Planner unit tests**: table-driven over the comparison matrix in 7.2, first sync, renames, deletions with partial reads, field independence, review withdrawal.
- **Fake source server** in `Toucan.Plugins.Testing`: `FakeSourceServer` with unit and document modes, revisions, pagination, and fault injection (latency, 429 with `Retry-After`, 5xx, timeout after commit, dropped page, duplicate delivery, concurrent remote edit, token expiry, async jobs).
- **Adapter conformance suite** (`SourceAdapterConformance`), shipped in `Toucan.Plugins.Testing` for adapter authors: runs against a fixture-backed or sandbox instance and checks pagination completeness, `IsComplete` honesty, idempotency handling, conflict reporting, error mapping, cancellation, and that all traffic goes through the host client.
- **Engine integration tests**: restart recovery from every journal state, ambiguous failures, duplicate prevention, cancellation mid-batch, multiple connections, pull during typing, undo of a pull, Save As with connections, project migration with `sources`.
- **Desktop tests**: wizard, plan dialog, conflict view, badges, accessibility checks for new screens.
- **CLI tests**: plan output, exit codes, non-interactive refusal, env-var tokens.
- **Live tests** for the first online connector against a sandbox project, opt-in by environment variable, not part of default CI.

## 12. Extensibility

- **New services** are new adapters: one class implementing the core plus the data interface that matches the API style, typically 300 to 800 lines.
- **New capabilities** are optional interfaces plus a flag in a minor API version; older adapters keep working and the host hides features they do not declare.
- **Declarative REST adapter** (post-1.0 in the roadmap): a built-in adapter configured by a JSON mapping (endpoints, paging, field paths). It is an ordinary adapter, so nothing in the host changes.
- **Toucan source protocol**: the reference REST adapter speaks a small, documented OpenAPI contract (section 13). Teams with their own string service implement the server side and need no plugin.
- **Change feeds and background sync** use contracts that ship in 1.3, consumed later.
- **Collaboration** (Toucan Hub, post-1.0) can be a source with role `primary` and a change feed; presence and locking stay separate.
- **Machine translation stays separate.** `ITranslationProvider` and AI backends are not sources. A service that offers both registers both.

## 13. Reference and first connectors

1. **Local folder** (`toucan.sources.local`, built-in module `Toucan.Modules.Sources`): document session over the existing reader and store. Used by the boundary tests in phase 4.
2. **Toucan source protocol** (`toucan.sources.rest`, built-in): unit session against a documented REST contract with ETags, cursor pagination, idempotency keys and a delta endpoint. Ships with a small reference server in `samples/` used by integration tests. This is the "documented custom REST service" option named in the roadmap.
3. **Sample connector** ported from its private `IRemoteSource` onto the public contract as the minimal third-party example.
4. **First commercial connector** (v0.29), chosen by demand. Lokalise fits the unit model and has a public OAuth flow, so it exercises the most of this design; Crowdin exercises the document model. The choice is an open decision (requirements, D1).

## 14. Phases

| Phase | Content | Roadmap |
|---|---|---|
| 0 | Prerequisites: targeted connection activation, `ReviewState.NeedsReview` in the plugin API, `sources` capability and `network` manifest block, OS secret store | v0.27 |
| 1 | API 1.3 contracts, registration, fake server, conformance suite skeleton | v0.27 |
| 2 | Accounts, connections, project schema, credential broker, auth flows, source HTTP client, CLI `accounts/login/test` | v0.27 |
| 3 | Local-folder document adapter, remote document store/reader wrappers, boundary and fidelity tests | v0.27 |
| 4 | Sync engine: state store, planner, executor, coordinator, recovery, delivery state, `ISourceSyncApi` | v0.28 |
| 5 | Desktop Sources area, wizard, plan dialog, conflict view, indicators, commands | v0.28 |
| 6 | Toucan source protocol adapter and reference server, sample connector port, full CLI | v0.28 |
| 7 | First commercial connector, live tests, docs for adapter authors | v0.29 |
| 8 | Hardening: performance at 10,000 keys × 10 locales, migrations, packaged end-to-end checks | v0.31 |

Each phase leaves `dotnet test Toucan.CrossPlatform.slnx` green and ships behind no feature flag except phase 5 UI, which stays hidden until phase 4 passes its recovery tests.
