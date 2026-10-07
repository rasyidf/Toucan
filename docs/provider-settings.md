---
title: "Provider Settings (App-level and Project-level)"
status: active
updated: 2026-10-07
summary: "Where provider settings and keys live (keys in the secret store), built-in provider defaults (Google, DeepL, Microsoft, AI, Custom), encryption model and the settings dialog."
---
# Provider Settings (App-level and Project-level)

This document explains how machine translation provider settings and credentials are stored and how to configure providers in Toucan. AI services (Claude, OpenAI, Gemini) are not translation providers: they are configured under Settings → AI, and machine translation reaches them through the **AI** provider. See [ai-integration.md](ai-integration.md).

## Where settings are stored

- Application-level settings: `Documents/Toucan/providers.json` in the user's home (the OS Documents folder on Windows, macOS, and Linux).
- Project-level settings: `<project folder>/.toucan/providers.json`

`providers.json` holds non-secret fields (Options) and the *names* of secret fields with empty values. The secret values (API keys) are in the secret store (`ISecretService`, see [Security model](#security-model)) under `mt/<provider>/<field>` for app-wide settings and `project/<id>/mt/<provider>/<field>` for a project's override, where `<id>` is a hash of the project folder. A project folder therefore never holds a key and is safe to commit. The JSON format is an array of ProviderSettings objects (see `Toucan.Core.Models.ProviderSettings`).

Files written by 0.19 and earlier have encrypted keys inline. Those are still read, and they move to the secret store the next time the provider settings are saved. Removing a provider (or a secret field) in the dialog removes its stored secret too.

## Built-in providers

All built-in providers are pre-populated with sensible default values when no saved settings exist:

| Provider | Options | Secrets | Default Values |
|----------|---------|---------|----------------|
| Google | — | api_key | — |
| DeepL | endpoint | api_key | endpoint=https://api.deepl.com/v2/translate |
| Microsoft | endpoint, region | api_key | endpoint=https://api.cognitive.microsofttranslator.com |
| AI | — (Settings → AI) | — (secret store `ai/<service>/api_key`) | — |
| Custom | endpoint, header_name | api_key | — |

Each provider declares its own schema through `ITranslationProvider.Definition`; `TranslationProviderRegistry` collects the definitions of all registered providers and exposes them via `ITranslationProviderRegistry`. Plugin providers appear in the same list (they are not marked built-in). Providers without a definition, such as the mock provider, work but are not listed.

### DeepL

DeepL has two plans with two hosts. Free-plan keys end in `:fx` and only work on `api-free.deepl.com`; Toucan switches to it for you when the key ends in `:fx` and the endpoint is the default paid one. The key is sent in the `Authorization: DeepL-Auth-Key` header. Language codes are adjusted for DeepL: the source is the bare code (`en-US` → `EN`), and targets keep a region only for English, Portuguese and Chinese variants (`fr-FR` → `FR`, `en` → `EN-US`, `zh-CN` → `ZH-HANS`). Formality More/Less is sent as `prefer_more`/`prefer_less`, so languages without a formal register fall back to the default instead of failing. Errors show DeepL's message, for example `HTTP 403 Forbidden: Wrong endpoint`.

### AI

The **AI** provider translates with the service chosen under Settings → AI (Claude, OpenAI or a compatible server, Gemini), using the editable Translate prompt. It has no provider settings, does not appear in the Provider Settings dialog, and is only offered while AI is turned on. Texts go in batches of up to 20 per target language as a JSON array. Placeholders are protected before sending and restored afterwards, like for every provider. Errors show the service's message, for example `HTTP 401 Unauthorized: invalid x-api-key`, or why AI cannot run ("AI is turned off", "… has no API key").

Up to 0.19, Claude, OpenAI and Gemini were separate providers here. Their settings and keys are moved to AI Integration on first run, and a project or preference that names one of them gets the AI provider. See [ai-integration.md](ai-integration.md#migration-from-019).

## Security model

- All secrets (provider keys and AI service keys) are in one file, `secrets.json`, in the user's application-data folder (`%APPDATA%\Toucan`, `~/Library/Application Support/Toucan`, `~/.config/Toucan`), written with owner-only (0600) permissions on macOS and Linux. Key names are in clear, values encrypted. Settings → Data & privacy lists the names and removes secrets.
- Windows: values are encrypted with the current user's DPAPI key, so they can only be read by that account on that machine (the same format the WPF app used).
- macOS and Linux: values are encrypted with AES-GCM using a random per-user key stored as `secret.key` next to `secrets.json`, with owner-only (0600) permissions. Encrypted values start with `aesgcm1:`. Anyone who can read both that key file and `secrets.json` can decrypt the secrets.
- If DPAPI was unavailable, old builds stored secrets base64-encoded inline in `providers.json`. Toucan still reads those values and moves them, encrypted, to the secret store the next time the provider settings are saved.
- For automated / CI scenarios you can still opt to use application-level settings or supply provider credentials in environment-specific locations; consider using OS-level secret stores for advanced scenarios.

## UI: Provider Settings dialog

You can open the Provider Settings dialog from two places:

- Options → Translation → Configure providers...
- Pre-Translate window (⚙ button next to provider dropdown)
- Project Properties → Translation → ⚙ button

The dialog supports:

- Viewing application- or project-level provider configurations (toggle project override and choose a folder).
- Adding & removing provider entries (dropdown of available providers with schema fields pre-populated).
- Editing options and secrets inline (schema fields are read-only keys, values are editable).
- Adding custom option/secret key-value pairs beyond the schema.
- Saving the provider list either to the application-level store or as a project-level override (`.toucan/providers.json`). Secret values go to the secret store in both cases.
