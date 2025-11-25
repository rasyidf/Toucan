# Provider Settings (App-level and Project-level)

This document explains how provider credentials are stored and how to configure providers in Toucan.

## Where settings are stored

- Application-level settings: %USERPROFILE%/Documents/Toucan/providers.json (uses the OS 'My Documents' path on Windows).
- Project-level settings: `<project folder>/.toucan/providers.json`

Provider settings JSON stores non-secret fields in cleartext (Options), and secrets (api keys) are encrypted using DPAPI before writing to disk. The JSON format is an array of ProviderSettings objects (see `Toucan.Core.Models.ProviderSettings`).

Example (after encryption):

```
[
  {
    "Provider": "DeepL",
    "Options": { "endpoint": "https://api.deepl.com/v2/translate" },
    "Secrets": { "api_key": "<ciphertext>" }
  }
]
```

## Security model

- Secrets are encrypted using the local machine or user DPAPI before being saved. This provides a simple, OS-backed: 'usable only on this account/machine' protection.
- For automated / CI scenarios you can still opt to use application-level settings or supply provider credentials in environment-specific locations; consider using OS-level secret stores for advanced scenarios.

## UI: Provider Settings dialog

You can open the Provider Settings dialog from two places:

- Options → Machine Translation → Configure providers...
- Pre-Translate window (⚙ button next to provider selection)

The dialog supports:

- Viewing application- or project-level provider configurations (toggle project override and choose a folder).
- Adding & removing provider entries.
- Viewing non-secret options and secret names; secrets are masked in the UI.
- Saving the provider list either to the application-level store or as a project-level override (`.toucan/providers.json`).

Note: current UI displays option/secret values read-only (for safety/simplicity). Editing secrets will be added in a follow-up where we add per-field editors and an obvious reveal/confirmation flow.
