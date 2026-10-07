---
title: "AI Integration"
status: active
updated: 2026-10-07
summary: "The app-wide AI switch, AI services (Claude, OpenAI-compatible, Gemini), the open and editable prompts of Translate, Analyze and Clarity, the secret store, and the migration from the old LLM providers."
---
# AI Integration

AI in Toucan is one integration with one switch, separate from machine translation providers.

- **AI Integration** (Settings → AI) is where you pick an AI service (Claude, OpenAI or a compatible server, Gemini), its model and key, and turn AI on or off for the whole app.
- **Machine translation** keeps the classic providers (Google, DeepL, Microsoft, a custom webhook). AI translation is one more entry, **AI**, which goes through AI Integration. It has no settings of its own.
- **Secrets** (every API key and token) live in one encrypted secret store in your user profile. Neither `ai.json` nor `providers.json` holds a key, and a project folder never does.

## The switch

AI is **off** by default. On first run, onboarding asks whether to turn it on, and you can change it later under Settings → AI → Use AI features.

While AI is off:
- `IAiService.CompleteAsync` throws before anything is sent, so no text leaves the machine for an AI service.
- The **AI** provider is not offered in the Translation panel. If it is still selected somewhere (Pre-translate, a project default), every item fails with "AI is turned off".
- Analyze and Check Source Clarity are disabled in the menu and hidden in the Issues panel.

Each feature also has its own switch under Settings → AI → Features and prompts.

## AI services

| Id | Service | Default endpoint | Default model | Key |
|---|---|---|---|---|
| `anthropic` | Claude (Anthropic Messages API) | `https://api.anthropic.com` | `claude-haiku-4-5-20251001` | required, `x-api-key` header; falls back to `ANTHROPIC_API_KEY` |
| `openai` | OpenAI / compatible (Chat Completions) | `https://api.openai.com/v1` | `gpt-4o-mini` | optional (local servers such as Ollama need none), Bearer token; falls back to `OPENAI_API_KEY` |
| `gemini` | Gemini (Generative Language API) | `https://generativelanguage.googleapis.com` | `gemini-flash-latest` | required, `x-goog-api-key` header (never in the URL); falls back to `GEMINI_API_KEY` |

Each service keeps its own endpoint and model, so switching services does not lose the other's settings. A feature can use a different model from the service's (for example a larger one for Analyze); set it in `ai.json` under `Features.<id>.Model`. **Test connection** sends a one-word request with the values as typed, before you save.

Services are `IAiBackend` implementations in `Toucan.Modules.Providers/Ai`. They only move text: `AiService` decides whether AI may run, which prompt to send, and which model and key to use.

## Features

| Feature | Where | What it sends | What it expects back |
|---|---|---|---|
| **Translate** (`translate`) | The **AI** translation provider: Translation panel, Pre-translate, quick translate, `toucan translate -p ai` | Up to 20 source texts as a JSON array, per target language | A JSON array of translations, same order and length |
| **Analyze** (`analyze`) | Translate → AI → Analyze Translations…, Issues panel | Numbered source and translation pairs, 10 per request | `[{index, severity, issue, suggestion, confidence}]` for items with a problem |
| **Clarity** (`clarity`) | Translate → AI → Check Source Clarity…, Issues panel | Numbered source strings (primary language) with their keys, 25 per request | `[{index, severity, issue, suggestion, note, confidence}]` for strings a translator could misread |

Analyze and Clarity findings go to the Issues panel. Analyze shows findings with a confidence of at least 0.6, Clarity at least 0.5. A suggested fix (a corrected translation, or a clearer source string) is applied with the panel's Apply button and can be undone. Clarity's note for translators is shown in the finding.

## Prompts

Every prompt Toucan sends is open and editable.

- **Built-in**: [`Toucan.Core/Ai/Prompts`](../Toucan.Core/Ai/Prompts) (`translate.md`, `analyze.md`, `clarity.md`), embedded in the app. Improvements to these files are welcome as pull requests.
- **Yours**: `Documents/Toucan/prompts/<feature>.md`, used in every project.
- **A project's own**: `<project>/.toucan/prompts/<feature>.md`. It has no secrets, so it is safe to commit and share with the team.

A project's version wins over yours, and yours wins over the built-in one. Edit prompts under Settings → AI → Edit prompt… (choose **All projects** or **This project**), or edit the files directly. **Reset to default** puts the built-in text back in the editor. **Remove saved version** deletes the file at that level. Saving your version unchanged from the built-in one deletes your file, so you keep getting improvements to the default.

### Template syntax

| Write | Becomes |
|---|---|
| `{{target_language}}` | The value |
| `{{#context}}…{{/context}}` | The text in between, only when `context` has a value |
| `{{^glossary}}…{{/glossary}}` | The text in between, only when `glossary` is empty |

Only the feature's own variables are replaced, so placeholders written as examples (`{{name}}`, `{0}`) are sent as written.

| Variable | Features | Value |
|---|---|---|
| `source_language` | all | Language code of the source text, e.g. `en-US` |
| `target_language` | Translate | Language code to translate into |
| `context` | all | Application context from Project Properties, else Settings → Machine translation |
| `formality` | Translate | Formality from the same places; empty for Default |
| `glossary` | Translate, Analyze | Approved terms, one `term → lang: translation` per line (empty until the glossary feature ships) |

### The reply contract

The prompt is free text, but Toucan reads the reply in the format in the last column of the Features table. The built-in prompts end with those format instructions. If you rewrite a prompt, keep them, or results come back empty ("No translation returned", no findings). Replies wrapped in a Markdown code fence, or with a sentence before the JSON, are still read.

## Secret store

`ISecretService` (`Toucan.Core/Services/SecretService.cs`) keeps every secret in `secrets.json` in the per-user application-data folder, next to `secret.key`. That folder is `%APPDATA%\Toucan` on Windows, `~/Library/Application Support/Toucan` on macOS and `~/.config/Toucan` on Linux. Values are encrypted with DPAPI on Windows, or AES-GCM with the per-user key elsewhere. Key names are stored in clear so they can be listed.

| Key | Holds |
|---|---|
| `ai/<service>/api_key` | An AI service's key, e.g. `ai/anthropic/api_key` |
| `mt/<provider>/<field>` | A translation provider's secret, e.g. `mt/deepl/api_key` |
| `project/<id>/mt/<provider>/<field>` | A project's own provider secret. `<id>` is a hash of the project folder, so the path is not stored. |

Settings → Data & privacy lists the stored names (never values) and removes them one by one or all at once. The CLI and the app share the store.

## CLI

`toucan translate <folder> -p ai` uses AI Integration. AI must be turned on in the app, or set `TOUCAN_AI_BACKEND=anthropic` (or `openai`, `gemini`) to turn it on for that run without changing saved settings. That is meant for CI, with the key in `ANTHROPIC_API_KEY` and so on. The project's `.toucan/prompts/translate.md` and its context are used. `-p claude`, `-p openai` and `-p gemini` still work and mean `-p ai`. Failed items print the reason, e.g. `2 × AI is turned off. Turn it on under Settings → AI.`

## Migration from 0.19

Up to 0.19, Claude, OpenAI and Gemini were translation providers in `providers.json`. The first time a newer Toucan loads AI settings (startup), it:

1. Moves their endpoint and model into `Documents/Toucan/ai.json` (values equal to the old defaults are dropped).
2. Moves their API keys into the secret store as `ai/<service>/api_key`.
3. Moves a custom `prompt` option into your Translate prompt (`Documents/Toucan/prompts/translate.md`), followed by the app context section that used to be appended.
4. Picks the service you last used for machine translation, removes the three entries from `providers.json`, and leaves AI **off** until you turn it on.

Other providers' keys stay readable where they are and move to the secret store the next time provider settings are saved. A project or preference that still names Claude, OpenAI or Gemini gets the AI provider. Project-level `providers.json` entries for the three are ignored.

## Code map

| Piece | Where |
|---|---|
| Contracts for modules and plugins: `IAiService`, `IAiBackend`, `AiFeatureDefinition`, `AiRequest` | `Toucan.Plugins.Abstractions` |
| `AiService`, `PromptLibrary`, `PromptTemplate`, `AiSettingsStore`, `LegacyAiMigration`, built-in features | `Toucan.Core/Services/Ai` |
| `SecretService`, `SecureStorageService`, `SecretKeys` | `Toucan.Core/Services`, `Toucan.Core/Contracts/ISecretService.cs` |
| Analyze, Clarity | `TranslationAnalyzerService`, `SourceClarityService` |
| Services and the AI translation provider | `Toucan.Modules.Providers/Ai`, `AiTranslationProvider.cs` |
| Registration | `AddToucanAi()` in `ToucanCoreServiceCollectionExtensions` |
| App: Settings → AI, prompt editor, onboarding, commands | `AiSettingsViewModel`, `PromptEditorViewModel`, `OnboardingViewModel`, `MainWindowViewModel.Ai.cs` |

Plugins cannot register AI services or features yet: the contracts exist, but `IPluginContext` has no `AddAiBackend`/`AddAiFeature`. That needs capability and id checks in the plugin host.
