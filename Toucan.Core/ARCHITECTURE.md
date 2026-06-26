# Toucan.Core — i18n Interop Architecture

Toucan is designed as a **universal i18n resource manager** that can import/export translation files across any framework, format, or tool ecosystem.

## Goal

Any i18n project from tools like **BabelEdit**, **inlang**, **Lokalise**, **Crowdin**, **i18next**, **Flutter ARB**, **Android strings.xml**, **iOS .strings**, etc. should be importable into Toucan and exportable back — losslessly where possible.

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

Each format pair is a strategy. The factory resolves by `SaveStyles` enum or by auto-detection.

## Supported & Planned Formats

| Format | Load | Save | Status |
|--------|------|------|--------|
| JSON (flat `{"key": "value"}`) | ✅ | ✅ | Done |
| JSON (nested `{"app": {"key": "value"}}`) | ✅ | ✅ | Done (NamespacedStrategy) |
| YAML | ❌ | ✅ | Save done, load stub |
| PO/POT (gettext) | ❌ | ✅ | Save done |
| INI / .properties | ❌ | ✅ | Save done |
| TOML | ❌ | ❌ | Planned |
| Android strings.xml | ❌ | ❌ | Planned |
| iOS .strings / .stringsdict | ❌ | ❌ | Planned |
| XLIFF 1.2 / 2.0 | ❌ | ❌ | Planned |
| ARB (Flutter) | ❌ | ❌ | Planned |
| CSV / TSV | ❌ | ❌ | Planned |
| .resx / .resw (.NET) | ❌ | ❌ | Planned |

## Project Manifest (`toucan.project`)

```json
{
  "$schema": "./toucan.project.schema.json",
  "primaryLanguage": "en-US",
  "languages": ["en-US", "fr-FR", "id-ID"],
  "saveStyle": "Json",
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

The manifest enables multi-package projects (e.g., separate `ui.json`, `errors.json`, `emails.json`).

## Interop with Other Tools

### Import from BabelEdit
BabelEdit uses nested JSON with one file per language. → Use `JsonLoadStrategy` with folder scan.

### Import from inlang
inlang uses a `project.inlang/settings.json` manifest pointing to message files. → Planned: `InlangLoadStrategy` reads the manifest and maps to TranslationItem.

### Import from i18next
i18next uses namespaced JSON files (`{ns}/{lang}.json`). → Use `NamespacedLoadStrategy` (already works).

### Import from Android
`res/values-{lang}/strings.xml` → Planned: `AndroidXmlLoadStrategy`.

### Export
Any loaded project can be exported to any supported save format via `ISaveStrategy`. The UI will offer "Export As..." with format selection.

## Adding a New Format

1. Create `MyFormatLoadStrategy : ILoadStrategy` in `Services/LoadStrategies/`
2. Create `MyFormatSaveStrategy : ISaveStrategy` in `Services/SaveStrategies/`
3. Add to `SaveStyles` enum
4. Register in DI (`App.axaml.cs`)

Each strategy is self-contained. No central parser needs modification.

## Streaming Parser (JsonParser)

For very large files, `JsonParser` in `Helpers/JsonHelper.cs` provides `IAsyncEnumerable<TranslationItem>` streaming. This avoids loading entire file contents into memory when files exceed typical sizes.

## Design Principles

- **Format-agnostic core** — TranslationItem is the universal atom
- **Lossless round-trip where possible** — preserve key ordering, comments (future)
- **Auto-detection** — `IProjectModeResolver` detects format from file contents/structure
- **Pluggable** — new formats = new strategy file, no core changes
- **Cross-platform** — Core targets net10.0 with zero platform dependencies
