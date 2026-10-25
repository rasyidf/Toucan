---
title: "BabelEdit compatibility review — Windows, 2026-10-21"
status: reference
updated: 2026-10-22
summary: "Historical Windows compatibility review from 2026-10-21. Current implementation and executed acceptance evidence are tracked in the resource-compatibility specification."
---

# BabelEdit compatibility review — Windows, 2026-10-21

The six compatibility failures reproduced in the original audit have been fixed
in this working tree. Broader 100% BabelEdit parity is still not certified; the
original comparison below records the baseline and remaining workflow coverage.

## Fix verification

- Empty and whitespace-only translations are retained by default. The project
  setting is passed through GUI, CLI and package save contexts. Opting out
  rewrites files even when their final translation becomes empty.
- Java properties support UTF-8 and Latin-1, honor an imported explicit encoding,
  and retain original filenames. Newline/output conversion no longer treats
  existing UTF-8 text as Latin-1.
- YAML loading strips/restores Rails locale wrappers, handles scalar and mapping
  sequences, and saves to the original `.yml`/`.yaml` path.
- Folders containing one `.babel` project now load its typed settings, concepts,
  comments, approvals and JSON package/file mappings. Saving creates a native
  `.tproj` and leaves `.babel` untouched. Both legacy converters retain direct
  folder/concept children and typed settings. Import rejects paths outside the
  chosen project folder; multiple `.babel` files need separate project folders.
- Excel import accepts BabelEdit's package/ID/per-language approval columns and
  older Toucan workbooks. Export writes the BabelEdit-compatible schema. Imports
  apply comment/approval changes even when text is unchanged, and native project
  metadata retains review state across reopening.
- JSON custom paths, matching IDs in multiple packages, metadata-only GUI Excel
  import, native save/reopen and Save As have regression coverage.

The current CLI corpus has **19 passing scenarios, zero lost keys**, including
both languages' Unicode, greeting placeholders, and isolated save/reload. The
core corpus tests also compare every loaded value before and after saving all
19 fixtures and assert that the original `.babel` bytes are unchanged. Current
test results are in `core-parity-final.trx` and `gui-parity-full.trx`; the gallery
reads their counts directly. `tools/Test-BabelConverters.py` checks both tools.
The final core suite passes **883 tests**; the full GUI suite passes **245 tests**.

The tests establish fixes for the six reported failures. They do not establish
all frameworks, source scanners, live providers, or Windows DPI combinations.

## Baseline and evidence

The installed application used for hands-on checks is **BabelEdit 5.5.1**.
The official documentation currently describes **5.7.0**; documentation-only
capabilities are kept separate from installed-app observations.
[Official documentation](https://www.codeandweb.com/babeledit/documentation).

The corpus contains **19 bilingual synthetic projects** under
[`samples/babeledit-parity`](../samples/babeledit-parity/README.md). One Generic
JSON project was created and saved entirely in BabelEdit. Eighteen projects
were generated from its `.babel` structure, using original synthetic content.
They are not eighteen independently completed project wizards.

Hands-on BabelEdit coverage: Generic JSON creation/language configuration/save;
ARB open/save; Java UTF-8 open/save and Unicode display; Rails YAML open/save and
Unicode display; YAML sequences open/save with both array values displayed;
RESX open/save; export format/language/metadata selection.
The remaining generated projects have Toucan CLI results but are not certified
by a BabelEdit UI round trip. No user project was edited. Live translation and
AI services were not invoked.

Local review artifacts:

- `build/babeledit-parity/evidence/`: screenshots and available accessibility trees.
- `build/babeledit-parity/toucan-read-results.json`: raw CLI results for every project.
- `build/babeledit-parity/roundtrip-*`: isolated Toucan save copies.
- `build/babeledit-parity/legacy-converter-output.json`: actual converter result.
- `build/babeledit-parity/babeledit-export.xlsx` and `babeledit-export-structure.json`: actual native export and inspected cell data.
- `build/babeledit-parity/tests/babel-format-baseline.trx`: **219 passed, zero failed**.
- `tools/New-BabelParityFixtures.py` and `tools/Test-BabelParityFixtures.py`: reproduction tools.

The original 219 tests are existing `FormatFidelityFixtureTests` and `FormatSupportTests`,
not 219 BabelEdit equivalence tests. The new audit executes the shipped Toucan
CLI using its normal composition root and an empty plugin directory. It checks
folder discovery, a two-language Unicode value, a greeting with a placeholder,
and key loss after save/reload. It now imports `.babel` settings when present,
uses the same project save service as the GUI, and fails on a changed Unicode
value, lost/added key, or failed save. Full workflow certification remains open.

## Original reproduced failures (six P1 findings fixed above)

| Priority | Finding | Reproduction / evidence | Required outcome |
|---|---|---|---|
| P1 | Empty translations disappear on Toucan CLI save | All 17 cases with a correct Unicode read lose `empty` (PHP: `messages.empty`) after an isolated save/reload. The seed has `save_empty_translations=true`; Toucan's normal folder loader does not read it. | Preserve empty keys by default; honor the project's empty-value setting through every save path. |
| P1 | UTF-8 Java properties become mojibake | Case 16 returns `æ…` instead of `日本語 — café 😀`; BabelEdit displays the original correctly (`11-java-utf8-unicode`). Toucan's `JavaPropertiesLoadStrategy` always reads Latin-1. | Persist input encoding explicitly and apply it on load as well as save; retain Java-compatible escaped Latin-1 as another option. |
| P1 | Rails locale roots become key prefixes | Case 12 produces 18 distinct keys such as `en-US.welcome` and `de-DE.welcome`, rather than nine shared keys. A lookup returns only one language. BabelEdit strips the locale root (`14-rails-unicode`). | Framework-aware parsing and writing of locale root wrappers, with correct shared key identity. |
| P1 | YAML sequences are silently missing | Case 18 exposes nine keys in Toucan instead of eleven. BabelEdit displays `choices.0=First` and `choices.1=Second` (`16-yaml-arrays-settled`). | Preserve sequences and their order, or block unsafe saves with an actionable warning. |
| P1 | Legacy `.babel` converter drops seed concepts | Running `tools/babel2toucan.py` against the UI-authored seed yields `packages: [{name: main, files: []}]`, despite nine concepts. It only handles `file_node` children in a package; the seed uses direct folder/concept children. | A tested migration into the current `.tproj` model, retaining concepts, comments, approvals, packages, paths, source language, and typed settings. |
| P1 | BabelEdit XLSX schema differs from Toucan's importer | The actual export headers are `Package`, `Translation ID`, `de-DE`, `de-DE approved`, `en-US`, `en-US approved`, `Comment`. Toucan's `ExcelService.Import` assumes column A is the key, treats subsequent headers as languages until `Comment`/`Approved`, and has only one approval column per key. This incompatibility is established by inspecting the export and importer source; the workbook was not imported into the Toucan GUI. | Detect BabelEdit workbooks explicitly; preserve package/key identity and approval per language; reject unsupported headers before applying changes. |
| P2 | Framework labels overstate integration evidence | Most JSON cases load through generic JSON. This does not test Transloco scopes, Vue SFC blocks, React extraction formats, or framework source scanning. | Separate a generic-format capability from a verified framework integration. |

Relevant implementation: `Toucan.Modules.Formats.Json/JsonSaveStrategy.cs`,
`Toucan.Modules.Formats.Json/ArbSaveStrategy.cs`,
`Toucan.Modules.Formats.Data/YamlLoadStrategy.cs`,
`Toucan.Modules.Formats.Text/JavaPropertiesLoadStrategy.cs`,
`Toucan.Core/Services/ProjectService.cs`, and `tools/babel2toucan.py`.
Several save strategies call `NoEmpty()` unconditionally. Project settings
expose `SaveEmptyTranslations` and `TranslationOrder`, but a settings property
alone does not prove it reaches the format writer.

## Original project and format comparison

“Reads” below means the narrow CLI checks passed, not lossless interoperability.
Every tested save with a successful Unicode read lost its empty key. Framework
JSON fixtures use one file per language; case 05 does **not** exercise a real
multi-namespace i18next directory layout.

| Case | BabelEdit framework / variant | Toucan observation | Remaining parity work |
|---|---|---|---|
| 01 | Generic JSON, flat dotted keys | Nine keys and both languages read; empty key lost on save | Preserve configured flat/nested layout, empties, nulls, order and indentation |
| 02 | Angular ngx-translate | Plain JSON reads | Framework scanning, extraction and output conventions |
| 03 | Angular Transloco | Plain JSON reads | Scoped resources, ID rules and scanning |
| 04 | Vue i18n JSON | Plain JSON reads | Vue-specific source patterns and SFC `<i18n>` resources remain untested |
| 05 | i18next | Plain JSON reads | Namespace directory fixture, plural conventions and duplicate-free saves |
| 06 | React JSON | Plain JSON reads | Separate react-intl, react-i18next, Lingui, and extraction-message schemas |
| 07 | Svelte i18n | Plain JSON reads | Source extraction and framework layout |
| 08 | Ember JSON | Plain JSON reads | Ember source patterns and layout |
| 09 | Laravel JSON | Plain JSON reads | Sentence IDs containing dots, flat output and Laravel placement |
| 10 | Generic YAML | Scalar map reads | Sequences, anchors, aliases, comments, tags and block formatting |
| 11 | Ember YAML | Scalar map reads | Framework extraction and YAML fidelity |
| 12 | Rails YAML | Fails shared-key identity | Strip/restore locale roots; Rails pluralization and source patterns |
| 13 | Laravel PHP | Nested package prefix `messages.*` reads | Comments, computed PHP values/code preservation and package placement |
| 14 | Flutter ARB | Ten keys and both languages read | Structured ICU/placeholder editing; exact metadata diff still required |
| 15 | Java Latin-1 with Unicode escapes | Reads correctly | Comments, file ordering, locale suffix variants and encoding selection |
| 16 | Java UTF-8 | Unicode read fails | Encoding-aware reader |
| 17 | JSON arrays, booleans, numbers, null | Thirteen editable keys read; null is not an editable key | Verify typed values, null preservation and exact array save fidelity |
| 18 | YAML sequences | Two sequence values missing | Sequence parsing and safe round trips |
| 19 | .NET RESX string resources | Nine keys and both languages read | Comments, non-string resources, headers, arbitrary basenames and package paths |
| Not exercised | Angular localize / XLIFF | Toucan implements XLIFF 1.2 and 2.0 | App-to-app fixtures with inline markup, IDs, notes, segmentation and source/target mapping |
| Not exercised | Gettext PO/POT | Toucan implements PO contexts, plurals, comments, references and fuzzy flags | BabelEdit-created PO round trip; obsolete entries and POT workflow |

The official framework list distinguishes Angular variants, Laravel PHP/JSON,
and React integrations. A shared extension is insufficient to establish a
framework match. [BabelEdit framework documentation](https://www.codeandweb.com/babeledit/documentation/framework-selection).

Toucan additionally exposes Android XML, iOS `.strings`, CSV, TOML and INI
export. These broaden Toucan's scope but do not compensate for a failed BabelEdit
workflow. Format limitations remain documented in [`formats.md`](formats.md).

## Original editor and workflow comparison

“Present” means visible in Toucan source/UI commands; it is not a new end-to-end
certification. “Unverified” stays open until the behavior is compared directly.

| Capability | BabelEdit baseline | Toucan evidence / assessment |
|---|---|---|
| Framework chooser | Installed chooser inspected, including expandable variants | Shared cards now improve consistency; variant-specific integration still partial |
| Add languages and explicit file selection | Exercised with English/German JSON | Language management and framework profiles present; arbitrary paths/packages need round-trip tests |
| Primary language / custom codes | Installed configuration inspected | `PrimaryLanguage`, aliases and custom paths present; .babel migration unverified |
| Project metadata and relative paths | `.babel` stores metadata beside real translations | Native `.tproj` exists; legacy conversion is incomplete and loses concepts |
| Save empty translations | Installed checkbox enabled in the seed | Reproduced writer gap; UI/default property is insufficient |
| File ordering and indentation | Installed configuration offers both | Writers sort output; preservation/indent control not certified |
| Tree/list views and language visibility | Installed tree and language toggles observed | Navigation, paging and language visibility present; side-by-side UX comparison remains |
| Add/rename/duplicate IDs | Installed toolbar/menu; add dialog inspected | Commands in `MainWindowViewModel.Edit.cs`; complete keyboard/focus flows need E2E |
| Search, filters and replace | Documented and installed controls visible | Search/replace service and approved/untranslated filters present; scope matching unverified |
| Comments and approval flags | Stored in `.babel`; approval controls visible | Comment sidecars and `IsApproved` present; cross-app metadata migration fails current converter |
| Bulk approval and visible selection scope | Documented | Bulk commands present; compare selected/all/current-view semantics and undo |
| Case and whitespace transforms | Documented | Lower/upper/sentence/title case, trim, trim lines and simplify present in Edit VM |
| Fill empty from another language | Documented copy workflow | “Fill Empty Values in View” invokes a translation provider; do not count that as a source-copy equivalent |
| Source references / external editor | Installed configuration has source root/editor fields | `SourceCodeService` and source scan UI present; framework-specific pattern parity unverified |
| Find unused IDs / extract missing IDs | Documented source integration | Source scan and unused-key filter present; source-to-new-ID extraction is not established. Compare scanner patterns, dynamic keys and ignores separately. “Delete Empty Keys” is a different operation. |
| Google, Microsoft and DeepL | Documented machine translation | Provider implementations present; no live requests made in this audit |
| AI consistency review | Documented suggestions with explanations and reset | `AiReviewRunner` and Analyze Translations commands present; quality and apply/reset semantics unverified |
| Similar phrase suggestions | Documented project-based similarity | Translation memory with exact/fuzzy matching and TMX present; ranking/context equivalence unverified |
| Spell checker | Documented dictionary-based editor feature | No corresponding spell-check implementation located in the reviewed source; gap pending contrary evidence |
| CSV import/export | Installed export offers separate Numbers/LibreOffice and Google Docs CSV options, selected languages/IDs, and metadata toggles | CSV format exists, but its documented writer drops extra columns; verify BabelEdit CSV dialects, comments and approval handling |
| Spreadsheet exchange | Installed 5.5.1 exported a native XLSX with package and per-language approval columns, despite the documentation describing CSV only | Toucan's header layout is incompatible; first item's approval is exported per key and applied to all languages on import. Needs an explicit BabelEdit adapter. |
| Statistics | Completion plus marketed word/cost reporting | `BulkActionService` reports completion counts; word/cost equivalent not located |
| Undo/redo and save/recovery | Needs app-to-app comparison | Undo recording, atomic save and recovery services present; not certified here |
| Windows theme and dialogs | Native dark chooser/configuration inspected | Earlier Toucan UI evidence has light/dark dialog captures; DPI, native pickers and populated E2E remain separate gates |

References: [project configuration](https://www.codeandweb.com/babeledit/documentation/project-configuration),
[editing tools](https://www.codeandweb.com/babeledit/documentation/editing-tools),
[CSV interchange](https://www.codeandweb.com/babeledit/documentation/import-export),
[spell checker](https://www.codeandweb.com/babeledit/documentation/spellchecker),
[similar phrases](https://www.codeandweb.com/babeledit/documentation/translation-memory),
[AI review](https://www.codeandweb.com/babeledit/documentation/consistency-ai).

Toucan workflow evidence: `Toucan.Avalonia/Views/MainMenu.cs`,
`Toucan.Avalonia/ViewModels/MainWindowViewModel.Edit.cs`,
`Toucan.Avalonia/ViewModels/MainWindowViewModel.Translation.cs`,
`Toucan.Core/Models/ProjectSettings.cs`, `Toucan.Core/Services/ExcelService.cs`,
`Toucan.Core/Services/TranslationMemoryService.cs`,
`Toucan.Core/Services/BulkActionService.cs`, and `Toucan.Modules.Providers`.

## Work required before a 100% claim

1. **Completed for the six reproduced failures:** file fidelity, encoding,
   Rails/sequences, native metadata migration, converters and Excel exchange
   now have regressions. Empty-value on/off, GUI/CLI saves and Save As are covered.
2. Create BabelEdit-authored XLIFF, PO/POT, multi-package, custom-path, custom-code,
   RTL, mixed-approval and large projects. Complete native open/edit/save/reopen
   for every generated framework case; preserve before/after snapshots.
3. Compare semantic content and metadata in both directions, not just key counts:
   comments, approval per language, namespaces, plurals, ICU parameters, arrays,
   nulls, numbers, booleans, Unicode, empties, paths, order and encoding.
4. Build populated Toucan E2E scenarios for import, editing, search/filter scope,
   keyboard actions, undo/redo, approvals, source references, CSV/XLSX and recovery.
   Screenshot every reachable dialog in light/dark and Windows scaling at
   100%, 125%, 150% and 200%. Validate clipping, focus, contrast and native pickers.
5. Verify provider and AI contracts with deterministic mocks; assess live provider
   equivalence separately. Add spell checking and word/cost reporting if the
   product's parity target includes them.
6. Publish an explicit versioned acceptance matrix. A cell passes only when its
   app-to-app fixture and E2E checks pass. Unverified cells must not be counted
   as supported. No meaningful overall percentage follows from this limited corpus.

The initial audit created the fixtures and gap inventory. The follow-up fixes
and their current verification are recorded at the top of this document. Full
Windows/UI/framework parity still requires the remaining acceptance checks.
