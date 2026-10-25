---
title: "Resource compatibility: UI closure and M7 delivery"
status: in-progress
progress: "7/8 delivery slices accepted; M7c platform/native gate remains open"
updated: 2026-10-23
summary: "U1–U5 and M7a–M7b implemented and verified on Windows. Artifact checks and CI wiring are in place; native/platform release rows remain open."
---

# Resource compatibility: UI closure and M7 delivery

This extends the [M0–M7 implementation plan](tasks.md). M1–M6 acceptance records remain evidence for the implemented contracts and tested workflows; they do not certify that every capability has a UI editor. Source inspection identified the UI gaps below on 2026-10-22. Implementation and Windows acceptance are recorded in [M7/UI implementation and verification](m7-ui.md). The platform/native release gate remains open.

## Scope and execution order

Reuse Project Properties, the import/open lifecycle, resource adapters, command registry, translation store, undo and recovery. Add optional provider capabilities where necessary instead of exposing settings that a writer ignores. Keep Toucan-owned labels and layouts. Preserve the existing M3–M6 working tree and historical evidence; committing it is a separate user instruction.

| Slice | Scope | Dependency |
|---|---|---|
| U1 | Resource inventory and serialization settings | Existing M1–M3 contracts |
| U2 | Resource mapping and editable target management | U1 |
| U3 | Structured ARB placeholder editor | Existing M3 metadata; U1 inspector |
| U4 | Migration preview and retained diagnostics | U1–U2 mapping validation |
| U5 | Generated-source explanations and scan diagnostic navigation | U1 resource details; existing M5–M6 |
| M7a | Engine evaluation, local pack registry and spelling service | Existing edit/locale contracts |
| M7b | Spelling preferences and correction UI | M7a; U3 metadata exclusions where applicable |
| M7c | Provenance, packaging and integrated release gate | U1–U5, M7a–M7b |

Implement U1 → U2 → U3 → U4 → U5 → M7a → M7b → M7c as reviewable, buildable changes. Engine evaluation may precede UI work, but does not authorize adding an unevaluated dependency. Automatic dictionary downloads, a full ICU editor and automatic deletion of source-unused keys are outside this delivery.

## U1 — Resource inventory and serialization settings

- [x] Add a Resources section to Project Properties listing exact path, stable ID, package, locale, format/dialect, ownership and provider restrictions, including empty documents. Show entry details that distinguish literal keys, nested paths and array indices without changing their persistence identity.
- [x] Add detached project defaults and per-resource overrides for the supported subset of layout, delimiter, arrays, indentation, order, Unicode escaping, input/output encoding including BOM, newline and empty values. Show effective value and its origin, with an explicit Inherit choice. Preserve unknown fields.
- [x] Reconcile the existing ordering/empty-value controls with typed settings so two controls cannot silently conflict. Detect provider support, validate unsupported combinations before applying, and explain legacy folder limitations. Use an additive optional settings capability if the current contracts cannot describe support accurately.
- [x] Preview representative serialized output through the actual writer against copies, with diagnostics and affected files. Apply settings through a dirty/recoverable project-settings operation; Cancel changes nothing. Encoding/layout changes that require re-reading must reload safely and must not discard unsaved translations.

Acceptance: users can configure supported settings without manifest edits; inheritance and explicit false values survive Save As/reopen. Invalid settings cannot mutate files. Preview uses no destination writes. Tests cover supported format differences, empty documents, unknown fields, cancellation and recovery of settings changes.

## U2 — Resource mappings and editable targets

- [x] Add validated add/edit/remove mapping actions for exact paths, locale and package, with format/dialect choices restricted to registered supported providers. Preserve stable resource IDs when changing destinations. Offer an explicit conversion flow when changing format semantics; a label change alone cannot convert a document.
- [x] Make the effect on existing entries explicit. Validate destination collisions, duplicate mappings, package identity, file existence and physical path boundaries before applying. Removal distinguishes detaching a mapping from deleting its file; never delete implicitly. Moves/conversions use existing save transactions and recovery.
- [x] Allow generated-catalog projects to configure editable target resources/locales without unlocking source keys. Keep generated ownership provider-enforced; no switch may make a protected source editable. Populate missing target units only from existing source identities.
- [x] Use one workspace/settings transaction with undo/redo and revision checks for accepted mapping changes. Require an explicit copy operation for external resources; rejected external paths provide an explanation and leave the workspace intact.

Acceptance: add a target locale to a FormatJS project, edit it, undo/redo, save/reopen and Save As without changing generated source bytes. Cover package collisions, last-entry/empty documents, cancelled changes, stale dialogs, unsafe paths and injected save failure. No arbitrary file is overwritten by remapping.

## U3 — ARB placeholder metadata

- [x] Add an ARB-specific metadata section alongside the existing description/comment editor. Edit placeholder names and supported fields such as type/example with validation; preserve unknown nested properties. Offer a validated JSON view for additional supported metadata rather than silently dropping it.
- [x] Define clear/remove behavior and show mismatches between message placeholders and metadata. Keep ICU messages as whole strings; unsupported transformations receive diagnostics instead of restructuring the message automatically.
- [x] Apply metadata through normal store notifications, undo/redo, dirty tracking and draft recovery. Respect provider metadata permissions and handle stale editor state.

Acceptance: metadata-only edits, explicit removal, rename, cancellation, undo/redo, recovery and native save/reopen preserve unrelated per-key and document metadata through the production UI composition.

## U4 — Migration preview and retained report

- [x] Split migration into read/validate, detached preview and explicit application through the existing lifecycle. Show source project, intended native manifest, packages/locales, exact destinations, generated roles, adapted/ignored settings and structured warnings/errors. Revalidate file changes before Apply.
- [x] Retain a reopenable report with severity, code, resource and field context; offer copy/export. Preserve unknown compatibility data. Avoid duplicating full source payloads or automatically including translation text in exported reports.
- [x] Ensure selecting a project file uses that exact selection rather than silently choosing another manifest in the same folder. Explain native-project precedence and require a visible choice when multiple candidates exist.

Acceptance: preview/cancel/validation failure leave active workspace and all files unchanged; successful migration preserves the original .babel file and reopens natively without it. Test multiple candidate files, changed inputs and partial save failure. Existing spreadsheet preview remains a separate, working exchange flow.

## U5 — Restriction explanations and scan findings

- [x] Display generated-source/read-only badges, the provider's reason and source path in editor/resource details. Explain unavailable structural commands accessibly, including keyboard and command-palette routes, while retaining backend enforcement.
- [x] Show scan diagnostic code, severity where supplied, file and line/column; make available locations navigable with the same safe path/editor handling as references. Findings without locations remain readable. Add clear complete/partial/cancelled status and a reason when unused filtering is disabled.
- [x] Localize added UI strings, provide keyboard navigation and accessible names for all U1–U5 controls, and verify practical dialog sizing/scrolling.

Acceptance: users can identify why a catalog is protected and navigate a dynamic-key finding to its source. Cancellation, project switches and stale scans cannot navigate or update the wrong project. Existing partial references remain visible; unresolved scans never enable unsafe unused classification.

## M7a — Local spelling engine and pack registry

- [x] Compare exact engine releases using authoritative upstream documentation: .NET/platform support, Hunspell compatibility, thread/cancellation behavior, maintenance and exact license/transitive artifacts. Record the decision and packaging implications before adding a dependency. No dependency is approved by this plan.
- [x] Implement a local registry for user-imported or individually approved .aff/.dic packs and optional .xcu DICT_SPELL records. Use deterministic locale aliases and visible fallback rules. Disable XML external entities; bound input sizes and reject paths outside the selected pack. If archives are supported, reject traversal, duplicate destinations and excessive expansion.
- [x] Implement cancellation-aware checking/suggestions with debounce and stale-result rejection. Exclude placeholders, ICU syntax and markup without excluding ordinary translatable text. Support persistent user/project word lists and session ignored terms with defined locale scope. Missing/failed dictionaries produce availability status, never misspelling results or save failures.

Acceptance: original fixtures cover aliases, fallback, malformed packs, encoding, exclusions, limits, cancellation, locale/text changes and list persistence. Checking works offline without BabelEdit; no automatic installation-directory discovery occurs.

## M7b — Spelling settings and editor corrections

- [x] Add dictionary import/remove/replace, locale assignment, fallback visibility, pack provenance/status and user/project word-list management using existing settings patterns. Keep local imports available independently of any future download catalog.
- [x] Add editor underlines and keyboard-accessible correction menus for suggestions, ignore and add-to-list. Respect generated-source permissions. Apply corrections through the same normal edit transaction so undo/redo, dirty state, validation and recovery agree.
- [x] Surface loading/error/missing-dictionary state without interrupting typing. Bound background work and suggestion counts; verify focus, selection and caret behavior after corrections. Keep all text and suggestion requests local.

Acceptance: correct a word, undo/redo, save/reopen and recover after restart; change language while checking; remove a pack while suggestions are pending. No stale underline or suggestion applies to new text/project. Generated sources stay protected and saving works without dictionaries.

## M7c — Provenance and integrated release gate

- [ ] For each exact engine/dictionary artifact, record upstream URL, pinned version, hash, chosen grant, notices, modifications and required source/compliance materials under the [component-specific policy](../../research/babeledit-resource-integration.md#resource-reuse-and-license-policy). Assess actual packaging modes and replacement requirements separately; a plugin boundary is not evidence by itself. User imports are not approved for redistribution automatically.
- [x] Generate notices from the inventory and verify release contents/hashes. Missing provenance excludes the affected distributed component; keep unrelated editing usable. Provide no BabelEdit proprietary code, assets or dictionary copies in fixtures or packages.
- [x] Run focused production-composition workflows for U1–U5 and M7, then the full cross-platform solution, existing 29 CLI scenarios, framework integration checks and unchanged frozen plugin. Preserve historical milestone evidence; add a new collector/report with executed commands, failures and artifact/source fingerprints.
- [ ] Execute packaged open/import/edit/save/Save As/recovery, native pickers, spelling and notices on each claimed supported OS/architecture and publishing mode. Record Windows/Linux/macOS rows separately; unavailable environments stay explicitly unverified and block claims for those rows. Inspect real UI accessibility/layout in addition to headless tests.
- [x] Update the acceptance matrix, supported settings/format docs and user help to match executed behavior. Record any remaining parser/format/spelling limits. Do not infer readiness from passing test counts alone.

Acceptance: every claimed workflow has executed evidence and no unresolved data-loss, permission-bypass or provenance blocker. The release report distinguishes implemented, headless-tested, packaged-tested and unsupported cases by platform. Automatic downloads remain deferred unless separately designed and approved through exact-artifact review.

## Completion accounting

Keep M0–M6 historical acceptance unchanged. Track U1–U5 here as additional required UI closure, and map M7a–M7c back to the original M7 checklist only when accepted. Update this document's delivery-slice count per accepted slice, with links to fixtures and execution evidence. The overall compatibility effort is complete only when both the original M7 gate and all five UI closure slices pass.

## Execution log

2026-10-23: Execution began with staged settings/history/recovery and resource UI, then metadata/migration/diagnostics, followed by engine evaluation and local spelling. Validate with focused xUnit v3/VSTest workflows, full regression/CLI checks, doc-status and packaging/provenance checks in that order. Platform claims remain limited to executed environments.

2026-10-23: U1–U5 and M7a–M7b accepted for the supported Windows production-composition/headless subset. Final full regression passes 1,384 tests; original CLI cases and framework/cycle checks pass. Source/notice guards and a CI publish matrix are implemented. M7c retains unexecuted native and cross-platform rows; see the report and machine-readable evidence.
