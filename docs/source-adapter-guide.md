---
title: "Writing a content-source adapter"
status: draft
updated: 2026-11-05
summary: "A walkthrough for plugin authors: the unit model with the sample connector, the document model with the Crowdin connector, testing with the kit, and what Toucan does for you."
---
# Writing a content-source adapter

A content source is a service outside the project that holds translations. Toucan owns accounts, credentials, mapping, sync state, conflicts, the Sources panel and the CLI. Your adapter only translates between that service and Toucan. The contract is in `Toucan.Plugins.Abstractions` (`Sources/`), version 1.3 of the plugin API; the reference text is in [plugins.md](plugins.md#content-sources).

Two working examples ship in this repository:

| | Model | Where | Lines |
|---|---|---|---|
| Sample | Units (key and value), in memory | `samples/Toucan.Sample.Connector` | about 150 |
| Crowdin | Documents (whole files with versions) | `connectors/Toucan.Connector.Crowdin` | about 400 |

## 1. Pick a model

- **Units** when the service stores keys and values and has an API for them (Lokalise, a string REST API). Implement `IUnitSourceSession`: `ReadUnitsAsync` and `WriteUnitsAsync`.
- **Documents** when the service stores files and Toucan should parse and write them with its own format strategies (Crowdin, a Git host, S3). Implement `IDocumentSourceSession`: `ListDocumentsAsync`, `ReadDocumentAsync`, `WriteDocumentAsync`, `DeleteDocumentAsync`.

A service may offer both; a session may implement both interfaces. The host picks the unit path when it finds `IUnitSourceSession`.

## 2. The plugin

```csharp
public sealed class CrowdinPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) => context.AddSourceAdapter(new CrowdinAdapter());
}
```

`plugin.json` declares API 1.3, the `sources` capability and every host the adapter may reach:

```json
{ "id": "crowdin.connector", "apiVersion": "1.3", "capabilities": ["sources"],
  "network": { "hosts": ["api.crowdin.com", "*.crowdin.com"], "userDefinedHosts": true } }
```

The adapter ID must start with the plugin ID and a dot (`crowdin.connector.api`). `userDefinedHosts` is needed only if users can enter their own server address (Crowdin Enterprise). Registration must not touch the network or the disk; open sessions lazily in `OpenSessionAsync`.

## 3. The descriptor

`SourceAdapterDescriptor` says what the adapter is and can do: display name, default address, sign-in methods, and the `SourceCapabilities` flags. The host never sends an operation outside the flags or outside what the credential may do (`SourceProjectInfo.Permissions` from `DescribeAsync`), so declare only what you implement and narrow in `DescribeAsync` for read-only tokens.

Sign-in is the host's job. Declare `ApiTokenAuth`, `BasicAuth`, `OAuthAuthorizationCodeAuth`, `OAuthDeviceCodeAuth` or `CustomAuth` and use `context.Http.Create()`: the client you get attaches the credential, refreshes it once on 401, enforces your declared hosts over HTTPS, retries idempotent requests and honors `Retry-After`. **The credential goes only to the account's own address.** A download link on another declared host (Crowdin serves exports from a storage host) is requested without it, which is also what pre-signed links require.

## 4. Reading

- Page results. Set `IsComplete` only on the last page, and only when the listing really finished. A read that ends without it is partial and Toucan infers no deletions from it.
- Version documents with something that changes when the content does. Crowdin has no per-translation revision, so its version is the SHA-256 of the exported bytes.
- Map a service's file layout to relative paths. Crowdin documents are the source file plus one per mapped language; the path follows the file's export pattern, or `{language}/{source path}`.

## 5. Writing

- Return one result per operation, in order, and never throw for a refusal. Use `Conflict` when the expected revision or version no longer matches.
- If the request may or may not have reached the service (a timeout after sending), return `Unknown`. Toucan then reads before it repeats the operation, and it reuses the operation ID so a service with idempotency keys sees one operation.
- Services without conditional writes (Crowdin) cannot close the race between the check and the write. Re-check just before writing and say so in your documentation.
- Refuse what the service cannot do, with a reason: Crowdin's connector does not create or delete files, and says "Create the file in Crowdin first."

## 6. Test it

`Toucan.Plugins.Testing` has everything for third-party authors.

```csharp
var report = await SourceAdapterConformance.RunAsync(adapter, manifest, fixture, ct);
report.ThrowIfFailed();
```

The suite checks the descriptor against the registration rules, pagination completeness, honest `IsComplete`, ordered results, conflict reporting, ambiguous failures, cancellation, and for documents the version rules. You supply an `ISourceConformanceFixture` that can seed and read the service (see `CrowdinConnectorTests` for a document fixture over a stand-in server, and `RestSourceTests` for a unit fixture over the reference server).

Go through the host's real HTTP pipeline in your tests, as the Crowdin tests do (`SourceHttpClientFactory` with a stub handler). It catches the host rules a hand-made `HttpClient` would let through: undeclared hosts, the credential on the wrong host, redirects.

Record real responses where you can; a stand-in written from documentation can be wrong. The Crowdin connector keeps an opt-in live test that reads from a real project with `TOUCAN_CROWDIN_TOKEN` and `TOUCAN_CROWDIN_PROJECT` set, and writes nothing.

## 7. What you get for free

Toucan runs the sync (`SyncPlanner`, `SyncExecutor`, journal, recovery), shows the plan and conflicts, tracks delivery per row, stores the token in the OS secret store, redacts it from diagnostics and offers `toucan sources pull|push|sync`. Your adapter does not keep state: the baselines and journals are the host's.

## 8. Install

Build, copy the output folder (with `plugin.json`) to the plugins folder, and trust the plugin when Toucan asks. The trust prompt lists `sources` and the hosts above; changing the `network` block asks again.

## 9. Known limits of the Crowdin connector (preview)

- Files are matched to local documents by relative path. Set an export pattern with a language placeholder in Crowdin so the paths match your project.
- Creating and deleting files stays in Crowdin.
- Local files outside the project's documents are never sent.
- A document's version needs one export per file and language, so large projects list slowly. Crowdin allows 20 simultaneous requests per account; the connector uses 2.
- The stand-in used in the tests follows Crowdin's API reference. Whether a file's own `exportOptions` carries an export pattern was not confirmed there (patterns are documented on directories, branches and bundles).
