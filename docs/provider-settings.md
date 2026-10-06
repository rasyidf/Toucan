# Provider Settings (App-level and Project-level)

This document explains how provider credentials are stored and how to configure providers in Toucan.

## Where settings are stored

- Application-level settings: `Documents/Toucan/providers.json` in the user's home (the OS Documents folder on Windows, macOS, and Linux).
- Project-level settings: `<project folder>/.toucan/providers.json`

Provider settings JSON stores non-secret fields in cleartext (Options), and secrets (api keys) are encrypted before writing to disk (see [Security model](#security-model)). Empty secrets are stored as empty strings (not encrypted). The JSON format is an array of ProviderSettings objects (see `Toucan.Core.Models.ProviderSettings`).

## Built-in providers

All built-in providers are pre-populated with sensible default values when no saved settings exist:

| Provider | Options | Secrets | Default Values |
|----------|---------|---------|----------------|
| Google | — | api_key | — |
| DeepL | endpoint | api_key | endpoint=https://api.deepl.com/v2/translate |
| Microsoft | endpoint, region | api_key | endpoint=https://api.cognitive.microsofttranslator.com |
| OpenAI | endpoint, model, prompt | api_key | endpoint=https://api.openai.com/v1, model=gpt-4o-mini |
| Claude | endpoint, model, prompt | api_key | endpoint=https://api.anthropic.com, model=claude-haiku-4-5-20251001 |
| Gemini | endpoint, model, prompt | api_key | endpoint=https://generativelanguage.googleapis.com, model=gemini-flash-latest |
| Custom | endpoint, header_name | api_key | — |

Each provider declares its own schema through `ITranslationProvider.Definition`; `TranslationProviderRegistry` collects the definitions of all registered providers and exposes them via `ITranslationProviderRegistry`. Plugin providers appear in the same list (they are not marked built-in). Providers without a definition, such as the mock provider, work but are not listed.

### DeepL

DeepL has two plans with two hosts. Free-plan keys end in `:fx` and only work on `api-free.deepl.com`; Toucan switches to it for you when the key ends in `:fx` and the endpoint is the default paid one. The key is sent in the `Authorization: DeepL-Auth-Key` header. Language codes are adjusted for DeepL: the source is the bare code (`en-US` → `EN`), and targets keep a region only for English, Portuguese and Chinese variants (`fr-FR` → `FR`, `en` → `EN-US`, `zh-CN` → `ZH-HANS`). Formality More/Less is sent as `prefer_more`/`prefer_less`, so languages without a formal register fall back to the default instead of failing. Errors show DeepL's message, for example `HTTP 403 Forbidden: Wrong endpoint`.

### Claude and Gemini

Both talk to the vendor's own API (not an OpenAI-compatible layer) and send up to 20 texts per request as a JSON array, in the order they appear.

- **Claude** needs an API key from the Anthropic Console (`console.anthropic.com`). A Claude subscription does not include API access; usage is billed from Console credit. The key is sent in the `x-api-key` header. Change `model` to use a larger model.
- **Gemini** works with a free key from Google AI Studio. Google may use free-tier prompts to improve its products, so do not send sensitive text on the free tier. The key is sent in the `x-goog-api-key` header, never in the URL. This is not the same as the **Google** provider, which is Google Cloud Translation and needs a Cloud project.
- If no key is saved, the provider reads `ANTHROPIC_API_KEY` or `GEMINI_API_KEY` from the environment.
- The app context and formality from Settings or Project Properties are added to the prompt. A custom `prompt` replaces the default instructions but keeps the context. Placeholders such as `{{name}}`, `{0}` and `%s` are kept as written by the default prompt.
- Errors show the HTTP status and the provider's message, for example `HTTP 401 Unauthorized: invalid x-api-key`.

## Security model

- Windows (WPF and Avalonia apps): secrets are encrypted with the current user's DPAPI key, so they can only be read by that account on that machine. Both apps use the same format, so the files are interchangeable.
- macOS and Linux (Avalonia app): secrets are encrypted with AES-GCM using a random per-user key stored as `Toucan/secret.key` in the user's application-data folder, with owner-only (0600) permissions. Encrypted values start with `aesgcm1:`. Anyone who can read both that key file and `providers.json` can decrypt the secrets.
- If DPAPI is unavailable, older builds stored secrets base64-encoded. Toucan still reads those values and writes them back encrypted the next time the provider settings are saved.
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
