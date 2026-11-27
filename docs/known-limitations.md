---
title: "Known Limitations"
status: active
updated: 2026-11-27
summary: "What Toucan 1.0 does not do, or does only as a preview: supported project size, formats that rewrite files, preview connectors and AI, and platform limits."
---
# Known Limitations

Toucan 1.0 is meant for daily translation work on projects up to the size below. This page lists what it does not do yet, or does only as a preview, so you can decide before you rely on it. Bugs that are being worked on are in [known issues](known-bugs.md); what changed in each version is in the [changelog](../CHANGELOG.md).

## Project size

- **Supported: up to 20,000 translations** (for example 2,000 keys in 10 languages). At that size a project opens in under 4 seconds, search answers in under half a second, and typing stays responsive while saving. Measured on a MacBook Air M1 with 8 GB; see [performance](performance.md).
- **Larger projects open with a notice.** They work, but each step is slower: at 100,000 translations (10,000 keys in 10 languages) opening takes about 11 seconds, each edit about 2 seconds, and saving every language at once can run out of memory. Save often, and split a very large project by namespace if you can.

## Formats

Every format and what it keeps is in the [formats table](formats.md). In short:

- **Edits in place** (Flutter ARB, JSON, i18next, Java `.properties`): a save changes only the entries you edited.
- **Rewrites the file** (YAML, PO, XLIFF, RESX, Android XML, iOS `.strings`, TOML, Laravel PHP, CSV): a save writes the whole file again. The values are kept, but comments and formatting in the file can change, and some constructs are not supported:
  - YAML: comments are not kept; anchors, aliases and tags block saving; block scalars are written back as quoted strings.
  - PO: obsolete (`#~`) entries are dropped.
  - Android XML: `<plurals>` and other resource types block saving.
  - RESX: non-string resources block saving, and per-entry comments are not kept.
  - iOS: `.stringsdict` plurals are not read.
  - Laravel PHP: computed values are dropped.
  - Project Properties shows which of the two modes the open project uses.
- **INI is export only**, and **FormatJS catalogs are read only** (they are generated from source code).
- **ICU plural and select messages** are edited as one string, not as separate plural forms.

## Online services

- **Crowdin is a preview connector.** Its tests run against a stand-in for Crowdin that simulates crashes, timeouts, permission errors and partial uploads. It has not yet been run against a live Crowdin project for those failure cases. Use it with a project you can restore, and review the sync plan before pushing.
- **Your own server through the Toucan source protocol** (`/v1`, [frozen for 1.0](source-protocol.md)) and **local folders** are supported.
- There is no Source Control panel: use your own Git client next to Toucan.

## AI and machine translation

- **AI features are a preview.** The AI providers (Anthropic, OpenAI, Gemini) and machine translation (DeepL, Google, Microsoft) are tested against simulated responses, not the live services, so a change on the provider's side can break a request. Every result opens in a preview, is applied only when you accept it, and can be undone.
- You bring your own API keys; the text you translate or analyse is sent to the provider you chose.

## Installing and updating

- **Windows builds are not code-signed** in 1.0. SmartScreen asks once per version; choose *More info → Run anyway*. Check the download against `SHA256SUMS.txt` if you want to be sure it is ours. See [install](install.md).
- **Release candidates for macOS may not be notarized.** macOS then asks you to allow the app once in *System Settings → Privacy & Security*. Final releases are signed and notarized.
- **Updates are not installed automatically.** Toucan checks for a new version at most once a day (you can turn this off in Options) and opens the release page; you download and install it yourself.
- **Linux without a Secret Service** (GNOME Keyring or KWallet) keeps the key that protects saved API keys and tokens in a file readable only by you, instead of in the keyring.

## Plugins

- Plugins run with your permissions, like any program you install. In 1.0 plugins are not signed, Toucan does not verify their publisher, and there is no plugin catalog: install only plugins you trust.
- The plugin API only grows within 1.x, so a plugin built for 1.0 keeps loading in later 1.x versions. See [compatibility](compatibility.md).

## Going back to an older version

Toucan 1.0 opens projects from every earlier release (0.23 and later) without changing them until you save. An older version never overwrites a file that a newer version wrote in a newer format; it refuses to open it and says to update. See [compatibility](compatibility.md) for what each version does.
