# `.babel` Format — Reference

> Historical reference for importing `.babel` project files into Toucan.

## Summary

The `.babel` format is an XML-based project file used by BabelEdit. It stores **metadata only** — actual translation texts live in framework-specific files (e.g., JSON, YAML). Toucan's `tools/babel2toucan.py` converts `.babel` → `toucan.project` JSON.

## Top-level Structure

```xml
<babeledit_project be_version="5.5.1" version="1.3">
  <framework>i18next</framework>
  <languages><language><code>en-US</code></language>...</languages>
  <primary_language>en-US</primary_language>
  <embedded_source_texts>false</embedded_source_texts>
  <translation_packages>...</translation_packages>
  <editor_configuration>...</editor_configuration>
  <configuration>...</configuration>
  <folder_node>...</folder_node>
</babeledit_project>
```

## Node Types

| Node | Purpose |
|------|---------|
| `package_node` | Groups related files (e.g., `main`) |
| `file_node` | Groups translation folders (e.g., `common`, `landing-page`) |
| `folder_node` | Hierarchical category grouping; can nest |
| `concept_node` | Actual translation key with `<name>`, `<description>`, `<comment>`, `<translations>` |

A `concept_node` contains `<translation>` elements with `<language>` and `<approved>` (boolean). The translated text itself is NOT stored in `.babel`.

## Key Metadata Fields

- `languages` — explicit listing of language codes
- `translation_packages` — maps packages to file paths per language (`translation_url` with `language` + `path`)
- `embedded_source_texts` — boolean; whether text is inline or external
- `primary_language` — main language for ordering/display
- `editor_configuration` — preferences (`save_empty_translations`, `translation_order`, `machine_translation_formality`)
- `configuration` — format info (`format`, `support_arrays`, `indent`)

## Mapping to `toucan.project`

```
concept_node → concept object
  translations → { "en-US": { "approved": false }, ... }
folder_node  → folder with nested concepts
file_node    → file grouping
package_node → package
```

Example JSON output:

```json
{
  "name": "buttons",
  "concepts": [
    {
      "name": "add",
      "translations": {
        "en-US": { "approved": false },
        "id-ID": { "approved": false }
      }
    }
  ]
}
```

## Conversion

```bash
python tools/babel2toucan.py path/to/project.babel > toucan.project
```

## FAQ

- **Does `toucan.project` include actual text?** — No, for parity with `.babel`. An optional `text` property is supported by the schema.
- **How to validate?** — Use `toucan.project.schema.json` with any JSON Schema validator.
