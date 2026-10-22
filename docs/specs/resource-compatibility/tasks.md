---
title: "Resource compatibility: implementation plan"
status: in-progress
progress: "3/8 milestones accepted; M0–M2 complete"
updated: 2026-10-22
summary: "M0–M2 accepted: baseline, resource contracts/identity, migration module and exact resource IO. M3–M7 remain: format fidelity, exchange, frameworks, scanning and spelling."
---

# Resource compatibility: implementation plan

This plan implements the gaps in [Current Toucan fit and remaining gaps](../../research/babeledit-resource-integration.md#current-toucan-fit-and-remaining-gaps). It preserves Toucan's own product direction and the report's resource/license boundaries. Milestone checkboxes record acceptance, not whether some code already exists.

## Outcome and scope

Toucan can migrate supported user-owned `.babel` projects into its native project format, edit the referenced resources without losing identity or metadata, and exchange package-aware spreadsheets. Shared file settings, framework capabilities, source scanning and spelling are native Toucan services, usable without BabelEdit installed.

Compatibility is measured by data preservation and valid framework output. No vendor JavaScript bridge, proprietary code port, vendor asset reuse, UI cloning, `.babel` write-back or full-feature parity is planned. Dictionary/engine artifacts must pass the existing component-specific provenance policy before shipping; this plan selects no new dependency and grants no new license approval.

The existing project IO/lifecycle, save transaction, draft recovery, command registry and plugin services remain the integration paths. Do not create a second project lifecycle or save system.

## Baseline to verify first

The source tree on 2026-10-21 contains additional work after the research snapshot:

- `ILoadStrategy` now has a default encoding-aware overload; `ProjectSettings.InputEncoding` and Java reader overrides exist.
- `ExcelService` now emits package/key columns and per-language approvals, with legacy import support. These paths still depend on `babel.*` item metadata.
- `BabelParityRegressionTests`, `BabelExcelTests` and `BabelImportFlowTests` include import, rename, explicit encoding, package and metadata cases.

Their presence is not a passing result. M0 must establish which gaps remain reproducible before scheduling fixes. In particular, do not reimplement spreadsheet approval or encoding fixes that already pass. The research report's eight numbered risks are historical observations to reconcile, not eight assumed current failures.

## Coverage and dependencies

| Gap area | Primary milestone | Evidence required |
|---|---|---|
| Formats / folder-only IO and package service | M1–M2 | Exact-file load/save with declared language and no sibling-file effects |
| Framework contracts and generated source ownership | M5 | Capability enforcement in UI and CLI with old profiles still working |
| Migration and explicit resource URLs | M2 | Imported project survives native save/reopen; original `.babel` unchanged |
| Save settings and key/document fidelity | M1–M3 | Distinct identities and selected settings survive edit/save/reopen |
| Spreadsheet exchange | M4 | Dialect detection, preview, package identity and mixed approval round trips |
| Source scanning | M6 | Framework-specific extraction with usage locations and cancellation |
| Spelling | M7 | Locale-aware corrections, placeholder exclusion, approved pack provenance |

Execution order: M0 → M1 → M2 → M3. M4 follows M2; M5 follows M1; M6 follows M5. M7 can proceed after M0 and the metadata/editing contracts it needs are stable. Land small reviewed changes in that order; independent readiness does not require separate agents.

## M0 — Establish the compatibility baseline

- [x] Record the current commit and relevant working-tree changes; preserve all existing work. Inventory original fixtures and separate them from vendor-owned examples/assets.
- [x] Run the existing import, format, encoding, exchange and recovery tests and reproduce unresolved report findings in temporary project copies. Record commands/results and classify each risk as open, fixed with evidence, unsupported with diagnostic, or not reproduced.
- [x] Create a versioned acceptance matrix by project-schema variant, resource dialect and workflow. Define the initial supported `.babel` subset from independently authored fixtures; unknown versions are not silently counted as supported.
- [x] Keep the existing behavior as the baseline for legacy native projects and published plugins. Capture their load/save and registration tests before contract changes.

**Acceptance:** each historical risk has a current disposition; passing existing fixes are retained; remaining milestones have named fixture cases. No vendor content becomes a distributable fixture. Record results in `docs/specs/resource-compatibility/acceptance.md` when implementation starts; do not invent passing evidence now.

**Accepted 2026-10-22:** see [M0 acceptance matrix and evidence](acceptance.md). Baseline `ea3d255` was clean; all 1,139 tests and 19 CLI cases pass. Ten isolated probes reconcile remaining risks. Existing compiled sample-plugin artifacts were frozen for future contract checks; a separately published historical plugin binary remains an explicit M1 verification limitation. M1–M7 are not accepted by these results.

## M1 — Add neutral resource and document contracts

Primary touchpoints: `Toucan.Plugins.Abstractions`, `TranslationItem`, `SaveContext`, `ProjectSettings`, `TranslationStrategyFactory`, tree/search/edit services and native project serialization.

- [x] Add an optional file-level read/write contract alongside existing folder strategies. Requests carry a resource descriptor, typed options and cancellation; results carry entries, document state and structured diagnostics.
- [x] Define stable resource/document identity, package identity, language, exact destination, format and dialect. Represent a literal key separately from a segmented object/array path. Display namespace strings must not be persistence identities.
- [x] Define typed serialization settings: layout, delimiter, array policy, indentation, ordering, input/output encoding, newline and empty-value policy. Set precedence to explicit resource option → project option → detected document state → format default. Import adapters translate external settings into this model; user overrides are explicit.
- [x] Keep opaque preserved data per document, including documents with zero editable entries. Do not copy large original payloads onto every item or treat a null value as an empty translation.
- [x] Introduce versioned native persistence and a compatibility envelope. Read old `BabelImportData`/`babel.*` state through migration; write new neutral state without requiring a destructive conversion. Preserve unknown compatibility fields or report them.
- [x] Centralize rename/move/delete/duplicate updates so identity, metadata, package/destination, undo and draft recovery move together. Update key comparisons in tree, search, review, translation memory lookup and bulk operations where needed; compatibility-only display prefixes must not leak into exported IDs.
- [x] Preserve old strategy/profile contracts. Resolve the optional file contract by registration/capability. If an old plugin cannot load a selected file exactly, report that limitation rather than scanning a directory and pretending it is exact-file loading. Verify an existing compiled sample plugin, not only a newly rebuilt one.

**Acceptance:** literal `a.b`, nested `a` / `b`, array indices and equal IDs in different packages remain distinct through editing, undo, draft recovery and native reopen. Legacy native projects/plugins retain their established behavior. Empty documents preserve their destination and document metadata.

**Accepted 2026-10-22:** [M1 implementation and evidence](m1.md). API 1.2 adds optional resource IO; JSON is the first document-aware adapter. Typed identity, document state, native migration, structural undo/recovery and the unchanged compiled M0 sample are covered by acceptance tests. Non-JSON adapters/importer separation remain M2; full format-option behavior remains M3.

## M2 — Isolate migration and use exact resource IO

Primary touchpoints: `BabelProjectReader`, `ProjectService`, `PackageService`, `ProjectLifecycleService`, composition roots and format modules.

- [x] Introduce `Toucan.Modules.Compatibility.BabelEdit` through a neutral compatibility-import contract. Move schema reading and mapping into the module; register it in Avalonia and CLI. Core depends on the contract, not the concrete module, and never switches on vendor framework names.
- [x] Parse XML with DTD/external resolution disabled. Validate the supported schema, required values, duplicate package/language mappings and missing files before changing the active workspace. Return diagnostics containing resource path and field information.
- [x] Produce explicit resource descriptors for every supported format. Replace the JSON-only import path and `PackageService`'s hard-coded JSON parsing with file-level dispatch. Never infer the declared language from an arbitrary basename or load unrelated siblings.
- [x] Start with JSON, YAML, ARB, Java properties and RESX exact-file paths. Add other already implemented dialects only when their file-level adapter and fixture pass; otherwise return an unsupported-resource diagnostic rather than a JSON fallback.
- [x] Preserve the current in-project path boundary. Normalize separators, handle platform case rules and resolve symlinks/reparse points before writes. External references produce an actionable diagnostic; copying external files is an explicit migration operation, not an automatic write outside the project.
- [x] Save to exact resource destinations and use the existing save transaction/recovery flow, including file deletion/truncation when the last entry is removed. Keep original `.babel` unchanged. Failed import leaves the current workspace untouched; failed multi-file save follows existing recovery guarantees.

**Acceptance:** each supported dialect handles an arbitrary basename, explicit locale, multiple packages, unrelated sibling files, missing files and path rejection. UI and CLI reopen the migrated native project with values, comments, descriptions and per-language approval intact. No test needs BabelEdit installed.

**Accepted 2026-10-22:** [M2 implementation and evidence](m2.md). All 1,201 tests pass; 19 corpus and six exact-resource CLI scenarios pass. The six supported format families preserve declared files/locales/packages through GUI and native reopen. Invalid migrations preserve the active workspace; physical path boundaries and journal ownership are verified. The two existing documentation-header errors and the matrix generator are repaired. M3–M7 remain open.

## M3 — Close document fidelity and settings gaps

Primary touchpoints: JSON/Data/Text format modules, ARB metadata editing, `ProjectTextFormat`, package saves and `SaveTransaction`.

- [ ] Apply M1 settings consistently to file loads, project/package saves, CLI mutations and Save As. Retain existing working input-encoding support and define conflicts with output overrides explicitly.
- [ ] Preserve JSON nulls, typed scalars, object/array distinctions and literal keys through document state. Numeric object keys become arrays only under an explicit supported policy; empty/sparse structures must not be guessed into a different shape.
- [ ] Preserve actual Rails locale wrappers through its dialect. Do not strip a locale-looking root from generic YAML without a declared dialect or a supported, explained detection result. Keep YAML constructs unsupported by the writer behind existing save guards.
- [ ] Map editable ARB descriptions to Toucan comments and structured placeholder metadata, merge these edits into original metadata, and retain unknown per-key and `@@` fields. Define removal behavior for deleted metadata and keys; unsupported ICU edits remain explicitly constrained.
- [ ] Respect declared Java encoding for ambiguous bytes and ASCII-only files. Retain escape policy, custom basename, comments/order where supported and selected newline settings. Reject invalid overrides with resource-specific diagnostics.
- [ ] Remove remaining cached imported-key authority once neutral identity is available. Verify rename/move, destination reassignment, last-entry deletion and new-entry placement across single- and multi-package projects.

**Acceptance:** semantic before/after fixtures cover all settings above, including empty-value policy on/off. No-op saves preserve supported data; formatting follows declared preservation support or an explicit setting. Unsupported constructs block unsafe overwrite with actionable diagnostics. Inject a save failure and confirm original data or recovery state remains usable.

## M4 — Finish spreadsheet compatibility without vendor coupling

Primary touchpoints: `ExcelService`, compatibility module, import preview/application flow and `BabelExcelTests`/`BabelImportFlowTests`.

- [ ] Retain verified existing mixed-approval and legacy import fixes. Separate the neutral tabular model from dialect-specific header mapping. Use M1 package/key identity instead of `babel.*` in the general Excel service.
- [ ] Offer explicit Toucan and BabelEdit-compatible export dialects; detect known import schemas and reject ambiguous, duplicate or unknown headers. Migration must not silently change the meaning of existing Toucan workbooks.
- [ ] Map values and approvals by language, with absent columns meaning unchanged. Distinguish an omitted cell from an explicitly cleared value through a documented preview policy. Match entries by package plus key, and diagnose duplicates/unknown packages/languages.
- [ ] Preview additions, edits, metadata-only edits, clears and conflicts. Apply accepted changes through one normal workspace edit transaction with undo, dirty tracking, validation and recovery. Cancellation changes nothing.
- [ ] Write translation text as spreadsheet text so strings beginning with formula characters remain text. Define workbook size limits and validate before loading the full edit set.

**Acceptance:** independently authored workbooks exercise reordered language columns, mixed approvals, repeated IDs in packages, Unicode, empty/absent cells, formula-like text, malformed headers and legacy import. Preview counts match committed changes; one undo restores them, and save/reopen retains metadata-only edits.

## M5 — Add framework settings and capability enforcement

Primary touchpoints: `IFrameworkProfile`, framework module, command registry, project settings UI and CLI handlers.

- [ ] Add optional companion contracts for typed settings, document capabilities and source-scanner selection. Reuse plugin settings/command infrastructure; do not add a second registration system. Existing profiles retain their default behavior.
- [ ] Derive capabilities from format, dialect, document/source ownership and provider support, not just a framework label. Enforce restrictions in the workspace operation layer and CLI as well as command availability in Avalonia.
- [ ] Support an independently implemented FormatJS extraction-catalog dialect using public framework specifications and original fixtures. Distinguish generated source catalogs from editable targets and preserve IDs/descriptions; do not flatten `defaultMessage` into an ordinary nested translation key.
- [ ] Expose only supported settings with Toucan-owned labels, defaults and help. Keep external setting-name translation in the compatibility module and diagnose ignored/unknown settings during import.

**Acceptance:** forbidden key/value operations cannot execute through UI, keyboard, bulk actions or CLI. Target editing remains available where allowed. Legacy plugins/profiles still discover and load resources. Generated catalogs survive import and reopen without becoming accidentally writable.

## M6 — Replace the fixed scanner with framework-selected extraction

Primary touchpoints: `SourceCodeService`, framework profiles, source-reference UI and scanner contracts in plugin abstractions.

- [ ] Define scanner requests/results with project/framework context, source globs/excludes, package/namespace context, structured key identity, file/line/column locations, diagnostics and cancellation. Keep the current generic scanner as a fallback with documented limits.
- [ ] Implement original rules from public syntax for i18next, Transloco, Vue, Rails and React/FormatJS. Cover multiline calls, JSX/template forms, options arguments and fallback lists. Use a parser where regex cannot give reliable bounds; review any new dependency before adding it.
- [ ] Preserve source namespace context and handle optional namespace resolution without marking a key in every package as used. Dynamic keys are unresolved findings, not evidence that other keys are unused.
- [ ] Respect project excludes and cancellation, avoid following directory cycles, bound regex/parser execution and report unreadable files without discarding a complete scan silently. Do not offer destructive unused-key cleanup from an incomplete scan.

**Acceptance:** original positive/negative fixtures for each framework assert keys and locations, including quoted text in comments, dynamic expressions and multiline input. Cancellation/incomplete scans are explicit; results for equal keys in separate namespaces do not merge. Record runtime on a representative source corpus before setting a performance budget.

## M7 — Add spelling and complete the release gate

Primary touchpoints: new dictionary registry/spelling service, Avalonia editor correction UI, validation integration, packaging and component provenance inventory.

- [ ] Select a spelling engine only after evaluating .NET/platform compatibility and the exact upstream license. Start with user-imported or individually approved packs; automatic language downloads are a separate completion item, not a prerequisite for local spelling.
- [ ] Import `.aff`/`.dic` and optional `.xcu` with locale aliases and explicit fallback rules. Select `DICT_SPELL` records only, resolve paths within the selected pack, disable XML external entities, bound file/archive sizes and reject archive traversal where archive import is offered.
- [ ] Provide cancellation-aware checking and suggestions, placeholder/ICU/markup exclusion, user/project word lists and ignored terms. Debounce editor checks and discard stale results after text or language changes. No dictionary or suggestion request leaves the device.
- [ ] Add Toucan-owned underlines/correction menus through normal undo/dirty tracking. Dictionary loading failures are actionable and do not block editing/saving. An absent dictionary is not a spelling error.
- [ ] Add provenance records, hashes, notices and required source/compliance materials for exact shipped/offered artifacts under the policy in the research report. Keep components replaceable as required by their actual grants; verify each packaging mode. Do not read BabelEdit's install directory automatically.
- [ ] Run the integrated acceptance matrix on supported platforms, verify plugin backward compatibility, packaged open/edit/save/recovery and notices, and update format/compatibility documentation to match evidence. Retain unsupported cases explicitly.

**Acceptance:** approved/user-imported packs work without BabelEdit, locale selection and placeholder exclusion are deterministic, corrections undo normally, and no proprietary vendor resources enter release packages. Missing provenance blocks the affected pack from distribution, not unrelated format work. Release claims identify the tested subset rather than a parity percentage.

## Delivery slices and verification

Use separate reviewable changes: (1) baseline/matrix, (2) additive contracts and identity migration, (3) compatibility module and exact-file IO, (4) JSON/YAML fidelity, (5) ARB/Java settings and metadata, (6) spreadsheet completion, (7) capabilities/FormatJS, (8) scanner service plus framework rule slices, (9) spelling core, (10) spelling UI/provenance, (11) integrated release evidence. Keep each slice buildable; adjust boundaries if API migration needs a coordinated change.

For each slice, run focused tests for the modified contract/workflow and required regression suites. For the baseline and final integration gate, run `dotnet test Toucan.CrossPlatform.slnx`; investigate failures against the recorded baseline. Use temporary fixtures for mutations and injected IO failures. Existing relevant suites include `FormatFidelityFixtureTests`, `FormatSupportTests`, `BabelParityRegressionTests`, `BabelExcelTests`, `BabelImportFlowTests`, `SaveRecoveryTests`, and plugin contract/registration tests.

Add tests only for observable data preservation, compatibility boundaries, failure behavior and user workflows. GUI evidence must cover import diagnostics/preview, metadata-only changes, capability restrictions, correction undo and save/reopen through production composition; a direct writer unit test cannot certify that path.

Do not mark a milestone accepted from source inspection or test names. Link each matrix row to its fixture, executed command/result and platform. New evidence updates the research report's historical gap dispositions and this plan's checklist. M0 acceptance is recorded above; no delivery dates or release-version commitments are implied.
