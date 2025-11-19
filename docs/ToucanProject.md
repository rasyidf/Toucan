# Toucan Project (`toucan.project`) — JSON schema documentation

This document describes the `toucan.project` JSON format used by Toucan to store translation metadata, file discovery, and editor preferences. This file is intended to be self-contained and independent from BabelEdit; however, it can mirror and import information from BabelEdit `.babel` files through the `tools/babel2toucan.py` converter.

---

## 🔎 Purpose

`toucan.project` is a small, human-readable, and machine-validated JSON manifest for translation projects.
It stores metadata for:
- Languages used in the project
- The organization of translation keys (packages/files/folders/concepts)
- Paths to language files and package-level translation URLs
- Editor and formatting preferences (useful for CI or automated checks)

It aims to be easy to diff and validate in Git workflows.

---

## 🧭 High-level structure

Top-level keys in `toucan.project`:
- `$schema` (optional, recommended): path or URL to the JSON schema used to validate the file.
- `schemaVersion` (string): schema version.
- `projectName` (string): friendly name for the project (ex: "qcash-ui").
- `framework` (string; optional): translation framework like "i18next".
- `languages` (array of strings): language codes (ex: `["en-US", "id-ID"]`). This is the preferred list of target languages.
- `translationPackages` (array): mapping packages to translation files. This is especially helpful to locate where the actual translation words live.
- `embeddedSourceTexts` (boolean): whether the translation texts are embedded in the project file.
- `primaryLanguage` (string; optional): main language (used for display, canonical ordering).
- `editorConfiguration` (object; optional): editor preferences (ex: `save_empty_translations`, `translation_order`) — flexible fields.
- `configuration` (object; optional): formatting preferences such as `format` (namespaced JSON vs nested) and `support_arrays`.
- `packages` (array): the hierarchical grouping of translation keys — packages contain files, files contain folders, folders contain concepts (keys).

---

## 📘 `translationPackages`

`translationPackages` is an array where each item includes:
- `name`: string
- `translationUrls`: array of objects each with:
  - `language`: language code
  - `path`: the file path (relative or absolute) where the project's translations for that language reside

Example:

{
  "name": "main",
  "translationUrls": [
    { "language": "en-US", "path": "./i18n/en.json" },
    { "language": "id-ID", "path": "./i18n/id.json" }
  ]
}

Use-case: this makes it possible to locate the translation JSON files for linting or quality checks.

---

## 🔑 `packages` structure (keys & groups)

- `packages` → `files` → `folders` → `concepts`.
- `concept` represents a translation key and contains metadata only; it does not require the translation text.

Example minimal `concept`:

{
  "name": "add",
  "description": "Context for the add button",
  "comment": "Shown in config dialog",
  "translations": {
    "en-US": {"approved": true},
    "id-ID": {"approved": false}
  }
}

Note: `translations` is a map keyed by language code; each value is an object that can contain:
- `approved` (boolean) — whether the translation has been approved
- `text` (string, optional) — actual translation text, if included. Toucan's default flow keeps translation text in the language files referenced by `translationPackages`. But storing `text` is allowed and can be used by tooling that merges texts.

---

## ✅ Example `toucan.project`

```json
{
  "$schema": "./toucan.project.schema.json",
  "schemaVersion": "1.0.0",
  "projectName": "qcash-ui",
  "framework": "i18next",
  "languages": ["en-US", "id-ID"],
  "translationPackages": [
    {"name": "main", "translationUrls": [
      {"language": "en-US", "path": "./i18n/en.json"},
      {"language": "id-ID", "path": "./i18n/id.json"}
    ]}
  ],
  "primaryLanguage": "en-US",
  "editorConfiguration": {"save_empty_translations": "true"},
  "packages": [
    {
      "name": "main",
      "files": [
        {
          "name": "common",
          "folders": [
            {
              "name": "buttons",
              "concepts": [
                {"name": "add", "translations": {"en-US": {"approved": false}}}
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

---

## 🧪 Validation & tooling

- The JSON Schema is at `toucan.project.schema.json` in this repository — use it to validate `toucan.project` files.
- Quick CLI validation (example using `ajv-cli`):

```pwsh
npm i -g ajv-cli
ajv validate -s ./toucan.project.schema.json -d ./toucan.project
```

- Alternatively, use Python (`jsonschema`) or other JSON schema validators (integrate into CI to fail the build on invalid files).

---

## 🧭 Use cases

- Centralized project metadata for CI checks: ensure every language has required keys, or verify `translationUrls` exist.
- Import into editor plugins that need a project-level definition for translations.
- Diff-friendly metadata: JSON diffs can be used in pull requests to review changes to translation keys or paths.

---

## ⚙️ Recommendations & best practices

- Maintain `languages` as the single source of truth for all language codes. If you use BabelEdit conversion, the converter populates `languages`, but keep them in `toucan.project` for clarity.
- Use `translationPackages` to point to the actual translation files; prefer relative paths to keep the project portable.
- Consider using `text` inside `translations` only when you want a single-file snapshot of translations — otherwise use the `translationPackages` to link to translation files.
- Use `primaryLanguage` for UI ordering and missing-translation fallbacks.

---

## 🔧 Extending the format

- `translationPackages[]` can be expanded with `format` to specify file type (json/yaml).
- `editorConfiguration` and `configuration` are intentionally flexible objects so you can add project-specific preferences. If you want stricter types, modify the `toucan.project.schema.json` accordingly.

---

## 📚 Related tools

- `tools/babel2toucan.py` — converter from BabelEdit `.babel` to `toucan.project` in this repository.
- `toucan.project.schema.json` — the JSON Schema for validation.

---

For feedback or special needs (like merging translation text into `toucan.project` automatically), I can add a merging option to the converter and a small test harness that verifies translations across all languages. Would you like that added next?  

