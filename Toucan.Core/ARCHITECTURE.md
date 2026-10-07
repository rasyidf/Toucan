---
title: "Toucan.Core — i18n Interop Architecture"
status: active
updated: 2026-10-07
summary: "How Toucan.Core normalizes every i18n format to a flat TranslationItem list and what import/export interop it targets."
---
# Toucan.Core — i18n Interop Architecture

Toucan is designed as a **universal i18n resource manager** that can import/export translation files across any framework, format, or tool ecosystem.

## Goal

Any i18n project from tools like **inlang**, **Lokalise**, **Crowdin**, **i18next**, **Flutter ARB**, **Android strings.xml**, **iOS .strings**, or legacy `.babel` projects should be importable into Toucan and exportable back — losslessly where possible.

## Internal Model

All formats normalize to a flat list of `TranslationItem`:

```
{ Language: "fr-FR", Namespace: "app.dialog.save_button", Value: "Sauvegarder" }
```

- **Language** — BCP-47 locale code
- **Namespace** — dot-separated key path (hierarchical keys flattened with `.`)
- **Value** — the translated string

This is the canonical format. All strategies convert to/from this.

## Strategy Pattern

```
ILoadStrategy   — reads a folder/file → IEnumerable<TranslationItem>
ISaveStrategy   — writes TranslationItem collection → folder/files
```

Each format pair is a strategy, identified by a string format ID (`FormatIds`, e.g. `json`, `android-xml`). The factory resolves by ID; `FormatDetector` auto-detects a folder's format from each strategy's `Detection` rule. Strategies also own the format's file-layout conventions (default path, language files, comment storage). Plugins add formats the same way. The contracts live in `Toucan.Plugins.Abstractions`.

## Supported Formats

All 14 built-in formats load and save except INI, which is save-only.

| Format | Format ID | Load | Save |
|--------|-----------|------|------|
| JSON (flat or nested) | `json` | ✅ | ✅ |
| JSON (namespaced / i18next) | `namespaced` | ✅ | ✅ |
| YAML | `yaml` | ✅ | ✅ |
| PO / POT (gettext) | `po` | ✅ | ✅ |
| INI | `ini` | ❌ | ✅ |
| Java `.properties` | `java-properties` | ✅ | ✅ |
| TOML | `toml` | ✅ | ✅ |
| Android `strings.xml` | `android-xml` | ✅ | ✅ |
| iOS `.strings` | `ios-strings` | ✅ | ✅ |
| XLIFF 1.2 / 2.0 | `xliff` | ✅ | ✅ |
| ARB (Flutter) | `arb` | ✅ | ✅ |
| CSV | `csv` | ✅ | ✅ |
| `.resx` / `.resw` (.NET) | `resx` | ✅ | ✅ |
| Laravel PHP arrays | `laravel-php` | ✅ | ✅ |
| Anything else | your plugin's ID | via plugin | via plugin |

## Project Manifest (`toucan.project`)

```json
{
  "$schema": "./toucan.project.schema.json",
  "primaryLanguage": "en-US",
  "languages": ["en-US", "fr-FR", "id-ID"],
  "saveFormat": "json",
  "translationPackages": [
    {
      "name": "main",
      "translationUrls": [
        { "language": "en-US", "path": "locales/en-US/main.json" },
        { "language": "fr-FR", "path": "locales/fr-FR/main.json" }
      ]
    }
  ]
}
```

`saveFormat` is a format ID (see above). Older files with `"saveStyle"` (a number or an enum name such as `"Json"`) are migrated when loaded and rewritten with `saveFormat` on the next save. An ID that no installed format provides makes the project fail to open with a "format unavailable" message instead of being read as JSON.

The manifest enables multi-package projects (e.g., separate `ui.json`, `errors.json`, `emails.json`).

## Interop with Other Tools

### Import from `.babel` format
The `.babel` format uses nested JSON with one file per language. → Use `JsonLoadStrategy` with folder scan.

### Import from inlang
inlang uses a `project.inlang/settings.json` manifest pointing to message files. → Planned: `InlangLoadStrategy` reads the manifest and maps to TranslationItem.

### Import from i18next
i18next uses namespaced JSON files (`{ns}/{lang}.json`). → Use `NamespacedLoadStrategy` (already works).

### Import from Android
`res/values-{lang}/strings.xml` → Planned: `AndroidXmlLoadStrategy`.

### Export
Any loaded project can be exported to any supported save format via `ISaveStrategy`. The UI will offer "Export As..." with format selection.

## Adding a New Format

Built into Toucan:

1. In the format family module that fits (`Toucan.Modules.Formats.Json`, `.Xml`, `.Text` or `.Data`), create `MyFormatLoadStrategy : ILoadStrategy` and `MyFormatSaveStrategy : ISaveStrategy`; both return the same `FormatId`.
2. Add the ID to `FormatIds` and implement `DefaultFilePath` (plus `FileExtensions`, `Detection`, … as needed) on the save strategy.
3. Register both with `AddFormatStrategy<…>()` in the module's `Formats<Family>Module.cs`, and add the ID to `ModuleSnapshotTests`.

As a plugin (no change to Toucan): see `docs/plugins.md` and `samples/Toucan.Sample.Plugin`.

Each strategy is self-contained. No central parser needs modification.

## Streaming Parser (JsonParser)

For very large files, `JsonParser` in `Helpers/JsonHelper.cs` provides `IAsyncEnumerable<TranslationItem>` streaming. This avoids loading entire file contents into memory when files exceed typical sizes.

## Design Principles

- **Format-agnostic core** — TranslationItem is the universal atom
- **Lossless round-trip where possible** — preserve key ordering, comments (future)
- **Auto-detection** — `IProjectModeResolver` detects format from file contents/structure
- **Pluggable** — new formats = new strategy file, no core changes
- **Cross-platform** — Core targets net10.0 with zero platform dependencies
