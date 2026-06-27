# `.babel` project file — reverse engineered format

This document describes the structure of a `.babel` project file and a JSON project format (`toucan.project`) proposed for Toucan.

## Summary

- The `.babel` format uses an XML-based project file for metadata describing translation keys, structure, and approval states.
- The .babel file contains *meta* data only — actual translation texts live in framework-specific files (e.g., JSON, YAML).
- Key structural elements: `package_node`, `file_node`, `folder_node`, `concept_node`, and `translation` entries.

## Top-level structure

Example (XML):

- `<babeledit_project be_version="5.5.1" version="1.3">` — root element with source tool version and file format version.
- `<framework>` — the target i18n framework (e.g., `i18next`).
- `<folder_node>` root which contains: `package_node` elements.

## Node types

- `package_node`: groups related files (ex: `main`).
- `file_node`: groups translation `folder_node`s (ex: `common`, `landing-page`).
- `folder_node`: hierarchical category grouping; can contain `concept_node` or more `folder_node` children.
- `concept_node`: an actual translation key (ex: `buttons.add`). It contains:
  - `<name>`: the key name.
  - `<description>`: optional description for context.
  - `<comment>`: optional developer comment.
  - `<translations>`: contains `translation` elements each with:
    - `<language>` (ex: `en-US`, `id-ID`, `zh-CN`).
    - `<approved>`: boolean string (`true`/`false`) — approval metadata.

> Note: The `.babel` file does NOT include the actual translated strings — it stores only metadata. The translations themselves are read/written into i18n source files (like `en.json`, `id.json`).

## Converting to JSON — `toucan.project`

This repository introduces `toucan.project` — a JSON file designed to store the same metadata in a human-friendly schema and to enable simple programmatic manipulations.

Example `toucan.project` structure (excerpt):

- `schemaVersion`: the toucan schema version
- `projectName`: human-friendly name
- `beVersion`: source tool version (mirrored from `.babel`)
- `framework`: i18n framework
- `languages`: array of languages declared in the `.babel` file
 - `languages`: array of languages; taken from the `<languages>` block (if present), or from the `translation` entries.
 - `translationPackages`: a mapping of `<translation_packages>` into JSON; contains `name` + `translationUrls` each with `language` and `path` fields.
 - `primaryLanguage`: mirrors `<primary_language>`; useful for UI and sorting.
 - `embeddedSourceTexts`: boolean mirrors `<embedded_source_texts>` — if true, the original text is included in the project file.
 - `editorConfiguration`: a general object of editor-related preferences such as `<translation_order>`, `save_empty_translations`, and `copy_templates`.
 - `configuration`: mirrors `<configuration>` entries (ex: `format`, `support_arrays`, `indent`)
- `packages`: list of package objects:
  - `name`
  - `files`: array of file objects
    - `name`, `folders`: array
      - `name`, `concepts` array
        - `name`, `description`, `comment`, `translations` map

This JSON schema is easier to validate, diff, and use with tooling.

## Implementation notes

- Each `concept_node` in `.babel` becomes a `concept` object in JSON. `translations` maps from language codes to a metadata object: `{ approved: boolean }`.
- We can optionally include the exact translation text if your toolchain points to a single source of truth, but the default `toucan.project` mirrors `.babel`: metadata-only.
- The JSON schema allows `externalKey` or `path` attributes to indicate where to find the actual strings (useful for CI or automated script to check missing translations).

## Tools provided

- `tools/babel2toucan.py`: small script that can parse a `.babel` file and create `toucan.project` JSON.

### Example mapping

Folder structure example in XML:

<folder_node>
  <name>buttons</name>
  <children>
    <concept_node><name>add</name>...</concept_node>
  </children>
</folder_node>

Becomes JSON:

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


---

## Frequently asked questions

- Q: Should `toucan.project` include actual text translations?  
  A: No — for parity with the `.babel` format it will not. However, adding an optional `text` property is supported by the schema and can be enabled during conversion.

- Q: How do I validate `toucan.project`?  
  A: Use the provided JSON Schema `toucan.project.schema.json` and any JSON Schema validator.

---

## Next steps
- You can convert existing `.babel` files by running `python tools/babel2toucan.py d:/locales/qcash-ui.babel > toucan.project`.
- Extend the `toucan.project` schema to include project-level metadata like `translationFiles` path and `ciCheckEnabled`.
