---
title: "Toucan compatibility layer: BabelEdit projects and shared resource standards"
status: reference
updated: 2026-10-22
summary: "Historical resource analysis and reuse boundaries; executed M0–M2 acceptance supersede current-status interpretations of the original gap inventory."
---

# Toucan compatibility layer: BabelEdit projects and shared resource standards

Date: 2026-10-21. Source inspected: `C:\Program Files\CodeAndWeb\BabelEdit\5.7.0\resources` and the current Toucan working tree.

This is a source and resource analysis, not an application round-trip certification. Existing uncommitted Toucan changes were inspected as part of the current baseline. No BabelEdit resources were modified, copied into Toucan, or executed. No application code was changed or tests run for this report.

## Product direction and compatibility boundary

Toucan remains an independent translation editor, with its own UX, architecture, settings and roadmap. The goal is reliable exchange of user-owned projects and translation resources. Feature-for-feature BabelEdit parity, host-API emulation, cloned dialogs, and matching implementation quirks are not acceptance criteria.

The compatibility layer translates external file schemas and settings into Toucan-owned contracts. Shared standards such as JSON, YAML, ARB, PO, properties and Hunspell stay useful independently of BabelEdit. BabelEdit-specific names and settings belong at the import/export boundary rather than becoming the native Toucan model.

Reuse the same **resource formats** and, where individually licensed, the same upstream dictionaries or libraries. Do not load or package BabelEdit's installed adapters, helper bundles, icons, demo text, UI assets, or proprietary configuration descriptions. Obtain reusable components from their original upstream sources with a pinned version and documented license. A local BabelEdit installation or user-selected path does not waive these requirements.

Proprietary behavior may inform a documented interoperability requirement, but implementation must be independently authored from the relevant public framework specification, user-owned exchange files and original fixtures. Translating proprietary JavaScript into C#, renaming symbols, or modifying copied code is not the adaptation proposed here. This analysis already inspected vendor source, so it does not claim a clean-room process or guarantee zero legal risk.

## Planned architecture

```text
User-owned .babel / exchange workbook
    -> Toucan.Modules.Compatibility.BabelEdit (proposed)
    -> neutral resource descriptors, metadata and diagnostics
    -> existing Toucan format modules and project/save services

Licensed upstream .aff/.dic/.xcu language pack
    -> shared dictionary registry and spelling service
    -> Toucan editor and validation interfaces
```

The proposed compatibility module handles external schema versions, paths, setting translation and unsupported-field diagnostics. It owns no BabelEdit runtime or copied adapter code. Native editing and saving continue through Toucan services. The proposed module name describes a design; this report does not create it.

Use a neutral resource descriptor with package identity, explicit language, relative path, format/dialect, typed options and opaque preserved document data. Keep the original import metadata in a versioned compatibility envelope; do not spread `babel.*` keys into new general-purpose contracts. Migrate the existing `BabelImportData`/`FormatData` representation incrementally without breaking current projects.

Initially support one-way project migration and explicit spreadsheet exchange. Do not imply `.babel` write-back support until it is implemented and verified. Unknown settings produce diagnostics; supported native resource data should survive without needing BabelEdit installed.

## What is in the resource directory

| Folder | Observed contents | Role |
|---|---|---|
| `fileio` | 17 top-level `.mjs` files, including the shared `common.mjs`; three bundled dependency assets | Framework descriptions and translation import/export logic |
| `scripts` | `excel-exporter.mjs`, `gettext-parser.mjs` | Spreadsheet and Gettext conversion helpers |
| `dict` | 43 files; English variants, French, Dutch, Portuguese, registrations and license/readme files | Hunspell spelling data and locale registration |
| `demo` | XML `.babel` project and three JSON language files | Example of project metadata separated from translation text |
| `documents` | EULA and third-party acknowledgements | Distribution notices and component provenance |
| `icons` | Three Windows `.ico` files | Application/document assets |

This directory is not the full application source. UI, orchestration, providers, and the host implementation are not exposed here. Missing `.mjs` adapters do not prove BabelEdit lacks a format: PO conversion, for example, has a separate helper in `scripts`.

The bundled demo identifies itself as BabelEdit **3.0.1**, project schema **1.3**, despite residing in the 5.7.0 installation. Treat it as one historical compatibility sample, not proof of every current schema feature.

## How the format adapters work

Adapters export `info` and `fileio`. `info` describes a framework; `fileio` implements parsing and serialization. Many frameworks instantiate the same `Common` class with different metadata. Others override its behavior.

`info` can declare:

- File extensions, language-file versus language-directory layout, and namespace/key delimiters.
- Source globs, excluded directories, source matching patterns and secondary extraction patterns.
- Copy templates and explicit plural suffixes.
- Format settings with names, UI labels, types, defaults and enum choices.
- Capabilities such as file creation, source-language requirements, a read-only source language, parameter editing, and whether adding/removing/renaming keys is permitted.

The observed import protocol is `fileio.importData(filecontent, fileformat)`. The adapter reports values and metadata through a global `babeledit` object. Calls found in the installed adapters are:

`checkAndReportJsonError`, `setFileFormat`, `addTranslation`, `addError`, `setComment`, `setDescription`, `setParameters`, and `proposeAsSourceLanguage`.

The observed export protocol is `prepareExport(filename, configuration[, language])`, repeated `saveTranslation(pathSegments, value)` calls, optional `setMetadata(...)`, and `getSaveFile([existingContent])`. These signatures come from adapter implementations; the native host's full call order and guarantees are not established by these files alone.

The important design choice is **path segments**, not a single flattened dotted key. JSON modes distinguish literal text IDs, flat dotted IDs displayed as a tree, and genuinely nested object keys. Their `nested-keys` and `split-join-keys` flags are independent.

### Observations to translate into independent requirements

The following table records research observations, not a list of vendor algorithms to port. Validate each desired behavior against the framework's own specification and Toucan's data-preservation goals before implementing it.

| Adapter | Behavior observed | Toucan implication |
|---|---|---|
| `common.mjs` | Distinguishes JSON layouts; handles strings/nulls; reconstructs contiguous numeric-key objects as arrays; selects indentation | Keep key layout, nulls, original structure and serialization settings explicit |
| `i18next.mjs` | Language directories, `:` namespace delimiter, optional source namespace, CLDR-style plural suffixes, JSX and fallback-list matching | A generic JSON reader and one source regex are insufficient |
| `angular-transloco.mjs` | HTML pipes/directives, translation functions and list calls | Add framework-specific extraction rather than just a framework card |
| `react-formatjs-extract.mjs` | Reads `defaultMessage` and descriptions, proposes source language, declares read-only source and disables key CRUD | Introduce an extraction-catalog dialect and enforce capabilities in commands |
| `rails-yaml.mjs` | Removes the first locale wrapper, restores a locale based on language/file name, simplifies certain plural objects | Persist the actual wrapper; avoid blindly applying its first-root heuristic to generic YAML |
| `arb.mjs` | Maps descriptions/placeholders to editor metadata; merges exported values into existing JSON while retaining unrelated metadata | Preserve opaque metadata and make comment/parameter edits update the ARB metadata |
| `prop-files.mjs` | Explicit UTF-8 or ISO-8859-1 settings; Unicode escaping follows the selected encoding | Persist encoding; detection alone cannot disambiguate every file |
| `laravel-php.mjs` | Parses an AST, accepts a single literal returned array and rejects unsupported expressions | Define a supported PHP subset and block unsafe writes; a full PHP runtime is unnecessary |

These adapters also have limitations. `Common` rebuilds JSON rather than preserving formatting, and the Rails adapter makes assumptions about locale structure. Toucan should preserve document structure and explicit locale mappings using its own implementation. Compatibility tests measure preserved user data and valid output, not identical formatting, heuristics or bugs.

## Project files and spreadsheet helpers

The demo `.babel` XML contains languages, primary language, package-to-resource URLs, comments and approval metadata in a node hierarchy, editor preferences and adapter configuration. Actual messages are in the referenced JSON files. The demo has `save_empty_translations=true`, alphabetical ordering, tab indentation, nested JSON and arrays enabled.

For Toucan, this maps to `ProjectSettings`, `TranslationPackages`, `BabelImportData`, and one `TranslationItem` per language/key. Preserve package identity separately from the displayed key and store approval per language. Conversion into Toucan is currently one-way: the inspected save path writes Toucan settings, not updated `.babel` XML.

`excel-exporter.mjs` embeds SheetJS and exports two small wrappers:

- `exportSheet(sheetData)` converts a two-dimensional cell array into a workbook with a `Translations` sheet and returns base64 workbook data.
- `importSheet(base64Content)` reads the first worksheet into a two-dimensional cell array.

The wrapper does **not** define BabelEdit's package/key/language/approval column schema. That schema belongs to its caller. The existing local parity report records an actual BabelEdit export with `Package`, `Translation ID`, language columns interleaved with `<language> approved`, and `Comment`.

`gettext-parser.mjs` similarly exposes `importPoFile` and `exportPoFile` around a bundled PO parser/compiler. Toucan already has PO and ClosedXML implementations; integrating their data contracts is more useful than embedding these large JavaScript bundles.

## Current Toucan fit and remaining gaps

Implementation work is tracked in the [gap closure plan](../specs/resource-compatibility/tasks.md). This section records the inspected baseline; that plan requires current verification before treating a finding as still open, including newer encoding and spreadsheet fixes.

**M2 update:** [migration isolation and exact resource IO are implemented](../specs/resource-compatibility/m2.md) for JSON, YAML, ARB, Java properties, RESX/RESW and Laravel PHP. Schema/mapping/path validation precedes workspace replacement; package IO uses declared files and locales. PO/XLIFF exact migration is explicitly unsupported. M3–M7 retain format fidelity, exchange and framework work.

**M1 update:** [neutral resource contracts and identity are implemented](../specs/resource-compatibility/m1.md), with JSON document-aware IO, native compatibility envelopes, structural undo/recovery and unchanged compiled sample-plugin verification. The historical observations below are not a current failure list; Subsequent milestone records describe remaining format/framework work.

**M0 update, 2026-10-22:** [the executed acceptance matrix](../specs/resource-compatibility/acceptance.md) supersedes the current-status interpretation of this historical inventory. At commit `ea3d255`, 1,139 tests and 19 CLI cases pass. JSON custom-path renames, per-language spreadsheet approval and explicit Java encoding are verified for their tested subsets. Isolated probes still reproduce non-JSON path/language errors, package IO routing errors, key collisions, ignored indentation/order, ARB description edit loss and JSON null loss. Unknown schemas import without warnings, and generic YAML can be mistaken for a locale-wrapped dialect. FormatJS extraction is explicitly rejected. These findings narrow implementation work rather than reopening passing fixes.

| Area | Current implementation inspected | Integration work |
|---|---|---|
| Formats | `ILoadStrategy.Load(folder)` and `ISaveStrategy.Save(path, SaveContext)` | Add an optional explicit-file loading contract with language, dialect, settings and diagnostics; preserve existing plugin compatibility |
| Frameworks | `IFrameworkProfile` covers discovery, paths, detection and default format | Add a companion capability/settings/source-scanner contract rather than breaking existing profiles |
| Migration | `BabelProjectReader` parses XML safely, reads packages and metadata; `ProjectService.LoadBabelJson` respects explicit JSON resource URLs | Generalize explicit-file imports to non-JSON formats and validate unsupported schema features before migration |
| Save settings | `SaveContext` carries empty-value policy and a JSON layout string | Carry typed delimiter/layout, indentation, array policy, encoding, ordering and original document state through both load and save |
| Spelling | No spelling integration located in the reviewed services/contracts | Add a dictionary registry, spell-check service and editor corrections |
| Exchange | `ExcelService` uses ClosedXML | Add an explicit BabelEdit workbook dialect with per-language approvals and package/key identity |
| Source scanning | `SourceCodeService` has a fixed multi-framework regex and fixed source extension/exclusion lists | Select scanners from the framework profile; support multiline input and structured extraction results |

Specific source-level risks in the current working tree:

1. **Non-JSON `.babel` paths and language codes.** `LoadProject` uses package URLs only for JSON imports. Other formats fall back to scanning the folder. Explicit `.babel` language mappings can therefore be lost when basenames differ, and unrelated files may be included.
2. **Package loading is still JSON-specific.** `PackageService.LoadPackage` selects a loader but then calls `NestedJsonParser` directly. Its save path passes the containing directory to the format writer without consistently supplying the URL's exact filename. A generic package service needs explicit resource destinations in both directions.
3. **Key collisions.** `NestedJsonParser` joins object paths with `.`. A literal property `a.b` and nested `a` / `b` collapse to the same identity. Prefixing package names into the displayed namespace introduces another ambiguity. Use a structured identity with package plus path segments, while keeping literal IDs distinct.
4. **Cached key metadata needs a rename contract.** The imported JSON saver prefers `FormatData["babel.key"]` over `Namespace`. Renames/moves must update this metadata together with package/path identity. The inspected code establishes this dependency; a failing rename flow has not been reproduced here.
5. **Configuration is retained but only partly applied.** `BabelImportData.Configuration` keeps XML values, but the reader collapses JSON `format` into flat/nested and `SaveContext` does not carry the whole configuration. Retention alone does not implement indentation, array policy or ordering. FormatJS extraction is also absent from the reader's framework mapping.
6. **ARB editable metadata is disconnected.** The loader preserves raw `@key` blocks in `FormatData`; the writer emits them unchanged. It does not map `TranslationItem.Comment` back into `description`. Preserving a block is different from supporting edits to it.
7. **Spreadsheet approvals are lossy.** `ExcelService.Export` takes approval from the first language item; import applies one shared approval to every language. Its current header assumptions do not match the recorded BabelEdit workbook schema.
8. **Nulls and detection remain incomplete.** The JSON walker omits nulls. The current Java reader attempts strict UTF-8 and falls back to Latin-1, but the persisted Babel encoding configuration is not passed into that reader. ASCII-only files cannot reveal their intended encoding through detection.

The older `docs/babeledit-parity-review.md` should not be treated as the exact current implementation state. The working tree now includes empty-value policy propagation, a C# Babel reader, Java encoding detection, YAML sequence parsing and locale-wrapper handling. Those changes reduce older gaps but were not re-certified by this analysis.

## Proposed compatibility sequence

1. **Resource identity and migration.** Introduce an optional file-level loader returning entries, diagnostics and document state. Resolve each `.babel` package URL once and pass its explicit language/dialect/settings to that loader. Keep the existing folder-level interface for plugins. Reject unsupported migration features with specific diagnostics.
2. **Typed settings and reliable saves.** Carry format settings and original document state through all save paths. Use package plus literal ID/path segments as identity; specify how rename, move, delete and newly created entries affect destination files. Preserve nulls and opaque metadata. Stage writes through Toucan's existing save/recovery mechanisms.
3. **Spreadsheet interoperability.** Detect BabelEdit headers before importing, map package/key and each language's value/approval separately, and preview validation errors before applying changes. Keep Toucan's existing workbook dialect available. Test duplicate keys across packages, mixed approvals, empty cells and Unicode.
4. **Framework behavior.** Add FormatJS extraction catalogs and capability-driven UI/CLI restrictions, then framework-specific scanners for i18next, Transloco, Vue, Rails and React. Author rules from public framework syntax and original source fixtures. Keep generated catalogs' source ownership explicit while retaining Toucan's own editing workflow. Do not port the vendor's regex strings, templates or placeholder language.
5. **Spell checking.** Add a shared core service plus Avalonia underlines/correction menus. Read `.xcu` locale mappings, load the `.aff`/`.dic` pair for `DICT_SPELL`, ignore unrelated thesaurus/hyphenation registrations, and support project/user dictionaries. Exclude placeholders/ICU syntax from checked text and offer on-demand language packs.

Use synthetic, independently authored compatibility fixtures. Acceptance checks should cover explicit paths with unrelated sibling files, same key in multiple packages, literal dots versus nested keys, arrays/nulls, selected Java encoding, Rails wrappers, ARB metadata edits, generated-source ownership, XLSX mixed approvals, and import → edit → save → reopen through both GUI and CLI. Existing parity fixtures are a starting point, not evidence that these cases pass. Retain historical parity reports as evidence, but do not use a parity percentage as a product or release target.

## Resource reuse and license policy

Toucan's existing MIT license remains the license for its own code. Third-party components keep their own applicable licenses and notices; they are not relabeled MIT by inclusion in Toucan. The presence of `COPYING_LGPL` files in a directory is not a license grant for every neighboring file.

| Resource category | Compatibility policy |
|---|---|
| User-owned translation files and `.babel` metadata | Parse as data with independently written readers; preserve paths, comments and approvals within the supported schema |
| Vendor `.mjs` adapters and bundled helper files | Excluded from runtime, repository dependencies and release packages; independently implement the needed file interoperability |
| Vendor icons, demo content, UI text and styling | Excluded; use original Toucan assets and synthetic fixture content |
| Verified permissively licensed upstream library/data | Eligible after checking the exact release, attribution requirements and transitive contents; prefer existing Toucan dependencies where adequate |
| Verified LGPL upstream component | Eligible with a component-specific compliance record, separate license/notices, required source availability and applicable modification/replacement or relinking provisions |
| Dictionary with MPL, GPL, mixed or dual licenses | Assess that exact dictionary/version and selected grant separately; do not assume it is LGPL because Hunspell supports it |
| Missing or unclear provenance/license | Excluded from bundling and automatic download until resolved; an optional plugin or download does not automatically remove obligations |

For an LGPL **library**, prefer deployment that keeps the library identifiable and replaceable, and verify that the actual .NET packaging/publishing mode satisfies the applicable license version. Single-file publishing, trimming, static linkage and native AOT require separate assessment; a plugin boundary alone is not proof of compliance. Provide the required notices, license texts and corresponding library source through a valid version-specific method, including Toucan's changes to that component. LGPL 2.1 and LGPL 3.0 have different terms. This is a release policy, not a blanket determination that a particular combination is compliant. See the [LGPL 2.1 text](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html), [LGPL 3.0 text](https://www.gnu.org/licenses/lgpl-3.0.html), and [GNU license FAQ](https://www.gnu.org/licenses/gpl-faq.en.html).

For dictionary **data**, document the license on the exact `.aff`, `.dic` and registration files rather than applying library-linking assumptions. The installed French readme states MPL 2.0, the Dutch notice offers alternative licenses, and the Portuguese notices describe several grants/components. These observations are evidence that a single folder-wide LGPL classification would be wrong; they are not approval to redistribute those installed copies.

A shared dictionary registry can read the same Hunspell formats without depending on BabelEdit. Source approved packs directly from upstream, install them separately from Toucan's code, show their provenance/license, and allow removal/replacement. Support a user-selected dictionary folder without scanning BabelEdit's installation automatically. Keep all needed notices with imported/distributed packs. Packaging and download routes must both follow the chosen grant.

Each shipped or offered pack/dependency needs a provenance record with component ID, upstream URL, pinned version, artifact hash, exact license expression and chosen dual-license option, copyright/notice files, modification status, source/source-offer details where required, and distribution mode. Unknown fields keep a component out of releases. Generate third-party notices from this inventory and check packaged artifacts against it.

No dependency or dictionary is approved by this report, and no third-party resource has been added. Release-time license assessment is still required for the exact chosen artifacts; these boundaries reduce avoidable exposure rather than promise absence of future claims.

## Compatibility acceptance criteria

- Toucan opens supported user-owned `.babel` projects and workbooks without BabelEdit installed or access to its resource folder.
- External schema terms are confined to the compatibility module/envelope; native editor behavior and UX remain Toucan-owned.
- Import diagnostics state unsupported fields and any expected data loss before edits are applied or files overwritten.
- Original fixtures verify semantic interoperability and data preservation, including formats supported by Toucan beyond BabelEdit.
- Release artifacts contain no unapproved BabelEdit resources; every bundled or automatically downloaded third-party component has an approved provenance/license record and required compliance materials.
- Changes to vendor internals do not change Toucan behavior unless a supported external schema actually changes.

## Resource provenance and references

The installation includes a BabelEdit EULA, third-party acknowledgements and dictionary-specific licenses. The local adapter observations above remain research context only. Public specifications, user-owned interchange data, independent code and specifically licensed upstream resources define the implementation boundary. This report does not establish redistribution rights for installed BabelEdit resources.

- [Official framework selection](https://www.codeandweb.com/babeledit/documentation/framework-selection)
- [Official project configuration](https://www.codeandweb.com/babeledit/documentation/project-configuration)
- [Official spell-check documentation](https://www.codeandweb.com/babeledit/documentation/spellchecker): confirms Hunspell compatibility and `.xcu`/`.aff`/`.dic` structure.
- Local evidence: `resources/fileio/*.mjs`, `resources/scripts/*.mjs`, `resources/demo/demo.babel`, `resources/dict/*/dictionaries.xcu`.
- Toucan evidence: `Toucan.Plugins.Abstractions/Contracts/{ILoadStrategy,ISaveStrategy,IFrameworkProfile}.cs`, `Toucan.Plugins.Abstractions/Models/{SaveContext,TranslationItem}.cs`, `Toucan.Core/Services/{BabelProjectReader,ProjectService,PackageService,ExcelService,SourceCodeService}.cs`, `Toucan.Core.Common/Services/NestedJsonParser.cs`, and JSON/Data/Text format modules.
