# Provider Settings (App-level and Project-level)

This document explains how provider credentials are stored and how to configure providers in Toucan.

## Where settings are stored

- Application-level settings: %USERPROFILE%/Documents/Toucan/providers.json (uses the OS 'My Documents' path on Windows).
- Project-level settings: `<project folder>/.toucan/providers.json`

Provider settings JSON stores non-secret fields in cleartext (Options), and secrets (api keys) are encrypted using DPAPI before writing to disk. Empty secrets are stored as empty strings (not encrypted). The JSON format is an array of ProviderSettings objects (see `Toucan.Core.Models.ProviderSettings`).

## Built-in providers

All built-in providers are pre-populated with sensible default values when no saved settings exist:

| Provider | Options | Secrets | Default Values |
|----------|---------|---------|----------------|
| Google | — | api_key | — |
| DeepL | endpoint | api_key | endpoint=https://api.deepl.com/v2/translate |
| Microsoft | endpoint, region | api_key | endpoint=https://api.cognitive.microsofttranslator.com |
| OpenAI | endpoint, model, prompt | api_key | endpoint=https://api.openai.com/v1, model=gpt-4o-mini |
| Custom | endpoint, header_name | api_key | — |

Provider schemas are defined in `TranslationProviderRegistry` and exposed via `ITranslationProviderRegistry`.

## Security model

- Secrets are encrypted using the local machine or user DPAPI before being saved. This provides a simple, OS-backed: 'usable only on this account/machine' protection.
- Empty secret values are NOT encrypted (stored as empty string in JSON) to avoid confusion when decrypting.
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
- Saving the provider list either to the application-level store or as a project-level override (`.toucan/providers.json`).
