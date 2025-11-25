# Pretranslation Engine — Design & Plan

This document describes the planned Pretranslation Engine for Toucan and the design decisions guiding implementation.

## Goals

- Provide a modular, extensible pre-translation engine that can plug in multiple providers (DeepL, Google Translate, other LSPs). 
- Support targeted pre-translation operations: single key, namespace (group), and whole-language pretranslation.
- Offer synchronous and asynchronous APIs suitable for both bulk operations and single item operations from the UI.
- Provide telemetry / progress reporting and cancellation support.

## High-level architecture

- Core interface (Toucan.Core): IPretranslationService — contract used by UI and higher-level modules.
- Provider adapters (Toucan.Core.Providers): implement provider interface ITranslationProvider, e.g. DeepLProvider, GoogleProvider, MockProvider.
- Operation model: PretranslationRequest (keys, namespace, language, options), PretranslationResult (success items, failures, skipped, error details).
- Registration: DI-friendly, configured at runtime via appsettings / UI selection.

## Required operations

- PreTranslateKey(projectId, language, key): translate a single key.
- PreTranslateNamespace(projectId, language, namespace): translate all keys within the specified namespace.
- PreTranslateLanguage(projectId, language): translate all untranslated keys (bulk) for the given language.
- Options: overwrite existing translations? minimal confidence threshold? provider-specific parameters (formal/informal tone, glossary support).

## UX integration points

- UI commands for single key, namespace and language operations.
- Per-language context menu item(s) on the Languages view to allow faster targeted actions instead of central "Advanced options".

## Implementation plan

1. Add new core interfaces and models under Toucan.Core.Contracts.
2. Implement a lightweight Mock Provider to test the workflow.
3. Implement a Provider adapter scaffold for DeepL/Google (configuration + simple HTTP client) — see follow-ups for credentials and rate-limit handling.
4. Add ViewModel support and UI wiring for per-key, per-namespace and per-language flows.
5. Add progress reporting and user feedback in UI (progress rings, notifications).

## Future priorities and considerations

- Provider credential storage and secure secrets handling.
- Rate limiting / batching strategies (to stay within provider quotas).
- Glossary and context hints per-key for improved accuracy.
- Undo/rollback or staging mode for pre-translated values before committing.
- Background worker with queueing to handle very large projects asynchronously.

## Notes

This design document is an initial plan. Implementation will be iterative and include unit tests and docs to show example usage in the app.

---
## Implementation so far

- Added core contracts: `IPretranslationService` and `ITranslationProvider`.
- Added a `PretranslationRequest`/`PretranslationResult` model with item-level results.
- Implemented a simple `PretranslationService` and registered it with DI.
- Added a `MockTranslationProvider` which produces synthetic translations for testing.
- Updated the existing `BulkActionService` to delegate to the `IPretranslationService` when present so existing UI code continues to work while using the new engine.

## How UI calls the engine today

- UI still calls bulk pre-translate via `IBulkActionService.PreTranslateAsync(IEnumerable<TranslationItem>)` — in this implementation `BulkActionService` will use the pretranslation engine when available.

## Next steps
## Tests

- Unit tests have been added in `tests/Toucan.Core.Tests` covering the mock provider and the `PretranslationService` integration.

Run tests from the repo root:

```pwsh
dotnet test
```


- Add adapters for real providers (DeepL, Google).
- Add robust batching and rate-limit handling, retries, and credentials for providers.
- Provide an admin UI to configure providers and default provider selection per project.

