# BabelEdit `.babel` — concise template analysis

This template (`template.babel`) is a minimal BabelEdit project file used to generate a consistent `.babel` from BabelEdit.

## Key nodes to mirror in JSON

- `languages`: explicit listing of languages available under `<languages><language><code>xx-XX</code></language>...`.
- `translation_packages`: list of `translation_package` each containing a `name` and `translation_urls` where each `translation_url` maps a language to a file path. This lets us reference actual translation resources.
- `embedded_source_texts`: boolean to indicate whether translations texts are embedded in the `.babel` or externally stored.
- `primary_language`: the main language used for ordering or display. Usually `en-US`.
- `editor_configuration`: preferences such as `save_empty_translations`, `translation_order`, `machine_translation_formality`.
- `configuration`: file format and JSON styling like `format` (e.g., `namespaced-json`), and `support_arrays`.

## Minimal JSON mappings for `toucan.project` (suggestions)

- `languages`: copies the `languages` codes from `.babel`.
- `translationPackages`: maps `translation_packages` with `name` and `translationUrls` for each language to paths.
- `primaryLanguage`: string for primary language.
- `embeddedSourceTexts`: boolean.
- `editorConfiguration`: flat object mirroring editor settings.
- `configuration`: small object with `format` and `supportArrays`.

These additions make `toucan.project` a comprehensive reflection of Babel metadata and useful for tooling and CI.

## Examples

See `toucan.project` file in the repository for an example with `languages` and `packages` represented. The converter `tools/babel2toucan.py` can be extended to include `translationPackages`.

