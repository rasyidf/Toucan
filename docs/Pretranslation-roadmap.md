# Pretranslation Engine — Roadmap & Planned Features

This file tracks planned features and priorities for the Pretranslation engine to help guide future work and QA.

High-level goals
- Support multiple provider adapters (DeepL, Google, Microsoft Translator, other TMS).
- Provide targeted pretranslation operations: single key, namespace (prefix), whole language.
- Support batching, rate-limiting, retries, auth and provider options.
- Provide a clear UX for progress, confirmation for big jobs, staging/preview, rollback, and logging.

Planned features (short-term, 0-3 months)
- Provider adapters:
  - Mock provider (done) — test/demo provider producing synthetic translations.
  - DeepL adapter (planned) — implement HTTP client and configuration handling.
  - Google Translate adapter (planned) — implement HTTP client or SDK integration.
- Provider selection UI — allow selection of default provider per project.
- Per-language/context menu integration (done) — UI actions for key/namespace/language.
- Basic progress and cancellation support in the UI.

Planned features (mid-term, 3-9 months)
- Batch-processing with concurrency limits and rate-limit handling.
- Provider credentials management (secure storage and per-project settings).
- Glossary, hints and per-key context injection for better translation quality.
- Pre-translation staging mode and diff/preview before committing changes.

Planned features (long-term, 9+ months)
- Background worker and queue processor for very large projects with persistence and job tracking.
- Audit logs of pre-translations, confidence scoring and review workflows.
- Integration with third-party TMS (crowdin, Lokalise) and glossary sync.

Tracking & acceptance criteria
- Add unit tests + integration tests for each provider and the central pretranslation service.
- Add UI e2e tests for per-language and bulk pre-translation scenarios.
- Document configuration & credential setup in the docs site.
