---
title: "Toucan source protocol"
status: active
updated: 2026-11-26
summary: "A small REST contract for a string service Toucan can sync with: ETag revisions, cursor pagination, idempotent operations, a delta endpoint and one error format."
---
# Toucan source protocol

This is the contract behind the built-in `toucan.sources.rest` adapter. A team with its own string service implements the server side and needs no plugin. The machine-readable form is [source-protocol.openapi.yaml](source-protocol.openapi.yaml). A runnable reference server is in `samples/Toucan.Sample.SourceServer`; the conformance suite in `Toucan.Plugins.Testing` runs against it in CI.

Version: `v1`, frozen with Toucan 1.0 (see [compatibility](compatibility.md)). All paths start with `/v1`. Bodies are JSON in UTF-8. Every request carries `Authorization: Bearer <token>`. Toucan sends HTTPS only (HTTP to a loopback address is allowed for local servers).

## Model

- A **project** holds **resources** (files, namespaces or bundles; use one fixed resource for a service with a single namespace) and **languages**.
- A **unit** is one translation: a key (`resource`, `name`, optional stable `id`) in one language, with a `value`, a `revision`, an optional review `status` and an optional `comment`.
- A **revision** is an opaque string. Toucan only compares it for equality and sends it back as `expectedRevision`. A server may use an ETag, a counter or a timestamp. The HTTP `ETag` header carries the same value on single-unit responses.
- The `id` of a key survives a rename. Without ids, a rename looks like a delete plus a create.

## Endpoints

| Method and path | Purpose |
|---|---|
| `GET /v1/projects` | Projects the token can see, for the project picker. |
| `GET /v1/projects/{project}` | Name, source language, languages, resources, what the token may do, limits. |
| `GET /v1/projects/{project}/units` | Units, one page at a time. Query: `resource`, `language` (repeatable), `cursor`, `limit`. |
| `GET /v1/projects/{project}/changes` | Units changed and deleted since a cursor. Query: `cursor`, `limit`. |
| `POST /v1/projects/{project}/operations` | A batch of writes. Each operation has an `id`. |

### Reading units

`GET .../units` returns:

```json
{
  "items": [
    { "key": { "resource": "app", "name": "home.title", "id": "k_17" }, "language": "fr", "value": "Accueil",
      "revision": "42", "status": { "value": "approved", "meaning": "approved" }, "comment": "Shown on the landing page" }
  ],
  "nextCursor": "c_9f2",
  "complete": false,
  "deltaCursor": null
}
```

- Follow `nextCursor` until it is `null`. The last page sets `"complete": true`. A reader that stops early, or a page that fails, is a partial read; Toucan then infers no deletions from it. Never set `complete` on a page unless the whole listing is finished.
- `deltaCursor` is set on the last page when the server supports `/changes`.
- `status.meaning` is one of `untranslated`, `draft`, `needsReview`, `reviewed`, `approved`, `rejected`. `status.value` is the server's own word, shown to people; Toucan writes a status by sending the meaning.

### Changes

`GET .../changes?cursor=…` returns `{ "changed": [unit…], "deleted": [{ "key": {…}, "language": "fr" | null }], "nextCursor", "complete", "cursor", "authoritativeDeletes" }`. Toucan never infers a deletion from a delta unless `authoritativeDeletes` is true; it confirms with a full read otherwise.

### Writing

`POST .../operations` takes `{ "operations": [ … ] }`, at most `limits.maxBatchSize`. Each operation has a client-chosen `id` (a GUID), a `type` and its fields:

| `type` | Fields |
|---|---|
| `putValue` | `key`, `language`, `value`, `expectedRevision?` |
| `putStatus` | `key`, `language`, `status` (one of the meanings above, in camelCase), `expectedRevision?` |
| `putComment` | `key`, `language`, `comment` |
| `createKey` | `key`, `values` (language → value) |
| `renameKey` | `key`, `newName` |
| `deleteKey` | `key` |
| `deleteTranslation` | `key`, `language`, `expectedRevision?` |

The response is `{ "results": [ … ] }` with one result per operation, **in the same order**:

```json
{ "id": "…", "outcome": "applied", "revision": "43", "remoteId": "k_18", "current": null, "error": null }
```

`outcome` is `applied`, `alreadyApplied`, `conflict`, `notFound`, `refused`, `rateLimited` or `failed`. A failed operation does not fail the request: the HTTP status stays 200 and the result says why. `conflict` returns the unit as the server holds it in `current`.

### Idempotency

The operation `id` is the idempotency key. A server that has already applied an `id` answers `alreadyApplied` with the revision it produced, and does nothing. Toucan reuses the same `id` when it retries an operation whose outcome it does not know (a timeout after sending), so a duplicate delivery never creates a second key or applies a value twice. Remember ids for at least 24 hours.

### Conditional writes

`expectedRevision` makes a write conditional: if the unit's revision differs, the result is `conflict`. Omit it only for a new key. `createKey` on an existing name is a `conflict`.

## Errors

Request-level failures use the status codes below and one body:

```json
{ "error": { "code": "rate_limited", "message": "Slow down.", "retryAfterSeconds": 3 } }
```

| HTTP | `code` | Toucan treats it as |
|---|---|---|
| 401 | `unauthenticated` | the token is missing or expired: sign in again |
| 403 | `forbidden` | signed in but not allowed |
| 404 | `not_found` | the project or resource does not exist |
| 409 | `conflict` | a conflict |
| 422 | `invalid` | refused content; retrying does not help |
| 429 | `rate_limited` | back off for `Retry-After` (or `retryAfterSeconds`) and retry |
| 5xx | `unavailable` | retry later |

## Limits

`GET .../projects/{project}` returns `limits`: `maxBatchSize`, `maxConcurrency`, `requestsPerMinute`. Toucan honors them. Omit a limit it does not need.

## Compatibility

New optional fields may appear in any response; ignore what you do not know. New operation types arrive only behind a new `permissions` flag, so an old server is never sent one.
