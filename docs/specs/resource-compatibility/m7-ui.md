---
title: "M7 and resource UI implementation"
status: active
updated: 2026-10-23
summary: "Resource/settings, metadata, migration and diagnostic UI plus local spelling are implemented. Windows tests and artifact checks are recorded; cross-platform/native release checks remain open."
---

# M7 and resource UI implementation

This implements U1–U5 and M7a–M7b from the [delivery plan](ui-closure-m7.md). M7c has artifact verification and CI wiring, but remains open for unexecuted platform and interactive packaged workflows. Historical M0–M6 evidence is unchanged. No commit or publication is part of this work.

## Resource management and settings

Project Properties now has a Resources page with stable resource IDs, exact paths, locales, packages, display prefixes, format/dialect and ownership. The editor tooltip also shows the resource path and typed key identity. Empty resource documents remain visible.

Project defaults and per-resource overrides expose only settings declared by the provider through additive `IResourceOptionsProvider`. Nullable choices mean Inherit. Effective values distinguish resource, typed project, legacy project, detected document and format-default origins. Existing ordering/empty-value controls are hidden for exact-resource projects to avoid competing editors. JSON, restricted YAML, ARB, Java properties and RESX declare supported fields; a provider without the companion retains IO but has no invented settings schema. `IResourceDialectProvider` supplies choices for newly added or explicitly converted JSON/YAML resources. Existing dialects require conversion rather than relabeling their contents.

A detached resource planner runs the actual writers in a private temporary project. It preserves typed paths and document state and shows affected files, output excerpts and structured diagnostics. It never writes preview output to project destinations. Apply rechecks workspace revision and file hashes. New destinations cannot overwrite unrelated existing files; physical project-path checks remain in force.

Mapping changes, settings, target additions, explicit conversions and detachments enter normal workspace history and dirty tracking. Display language aliases remain separate from file locales through preview, save/reopen and spelling selection. Configuration drafts are recoverable. Retargeting copies on Save and retains the original file; detachment removes the mapping without deleting its file. Explicit conversions copy editable text/review state into another writer and retain the original format-specific data in the original file. Ambiguous flattening of literal/nested keys is refused. Conversion provenance records the original descriptor and hash. Save As copies configured resources; detached conversion originals remain in the original project folder.

External references remain rejected as destinations. A separate, explicit Copy external resource action reads a selected file into a staged in-project mapping, validates it through its provider and retains the external original. Copies are bounded to 20 MiB. Input encoding changes require saved edits and an explicit Reload input choice; the preview shows the reinterpreted text. Output encoding changes do not reinterpret pending edits.

Generated source mappings remain protected. A FormatJS project can add editable targets/locales populated from existing source IDs without unlocking source keys. JSON target writers now enforce the same literal ID-to-string requirement as their readers. New/converted Rails YAML resources write their declared locale wrapper; existing valid locale-root variants remain preserved.

## Metadata, migration and diagnostics

ARB entries have a structured placeholder editor with name/type/example fields, an advanced validated JSON view, explicit removal and mismatch hints. Unknown nested metadata is retained. The optional `IResourceMetadataAuthority` declaration makes ARB file-owned fields and inline descriptions authoritative on native reopen, including external field removal; cached native copies cannot replace newer file metadata. Metadata-only changes use targeted undo/redo, dirty tracking and recovery. Existing ICU messages remain whole strings. The editor accepts unique ASCII identifier names and bounded JSON; it does not implement arbitrary ICU restructuring.

Opening a migration project shows a detached report of resource mappings, locales/packages, generated roles, project options and import diagnostics. Cancel leaves files and the active workspace untouched. Explicit file selections reach the exact reader; multiple candidates and native-project precedence have a visible selection step. Source/resource hashes are rechecked during lifecycle staging before replacement. The report remains available from Help after native save/reopen, and can be copied/exported without adding translation values or original document payloads deliberately.

Read-only resource badges and provider reasons explain generated restrictions. Source findings show their code, message and available file/line/column, with navigation using the same resolver as references. Complete, partial and cancelled scans remain distinct; unresolved scans cannot enable unused-key classification. Editor templates now parse executable/arguments before location substitution, avoiding automatic shell interpretation of filenames. Shell expressions in old editor templates require an executable/argument configuration instead. Native editor-launch behavior remains a platform verification item.

Added UI labels are localized, with Indonesian entries. Resource/spelling pages were rendered headlessly in both themes at the normal dialog size. Structured ARB controls are exercised through their actual Apply button, in addition to view-model/lifecycle tests.

## Local spelling

The [engine decision](m7-engine.md) pins unmodified WeCantSpell.Hunspell 7.0.1 under its MPL 1.1 alternative. The engine is independent of BabelEdit. No dictionary is bundled and no automatic download catalog is offered.

Project Properties has a Spelling page for local folder imports, removal/replacement, locale assignment, explicit fallback and user/project word lists. Plain `.aff`/`.dic` pairs use a locale filename or a user-specified locale; `.xcu` registrations import only `DICT_SPELL` records. Registration paths remain within the selected pack; external XML entities are prohibited. Imported bytes and notice hashes are recorded. Packs remain unverified user imports, with no redistribution approval implied by import. Registry removal detaches the pack while retaining copied local files. Imports/library removals and user lists apply immediately; project settings remain staged until the properties dialog is saved.

Limits include 64 registered packs, 16 registration files per import, 32 locales per record, 4 MiB affix files, 32 MiB dictionary files, 64 MiB combined dictionary input and 32 bounded notice files. Runtime loading checks affix/dictionary hashes, rejects empty dictionaries and parse warnings, and supports legacy dictionary encodings through the .NET code-page provider. Loading and checking have cancellation and time limits; absent or failed packs produce availability status, never misspelling errors or save failures.

Locale matching is deterministic: exact normalized locale, explicit assignment, or an explicit fallback. Ambiguous packs require a choice; no arbitrary regional fallback is selected. Lists and session ignores are locale-scoped. User/project lists allow at most 10,000 individual words per locale, each at most 128 characters. Project-list changes have targeted configuration history instead of copying resource payloads into each word-list edit.

Instantiated editor rows schedule debounced checks. Text/layout changes, dictionary changes, project changes and disposal cancel or discard stale results. An overlay draws underlines from the real wrapped/RTL text layout. Context menus offer up to six suggestions, session ignore and user/project word-list actions. Corrections use normal value edits and separate undo steps; generated/audit editors remain protected. Availability appears in editor details and the spelling settings page.

Natural-text extraction excludes common brace, printf, Qt, Laravel, markup/entity, URL and inline-code syntax. ICU plural/select branch prose is checked while argument names, selectors and formatting syntax are excluded. Text is bounded to 65,536 UTF-16 characters, 8,192 tokens and 24 ICU nesting levels; brace matching is precomputed to avoid nested rescanning. Each result holds at most 128 findings, with a three-second overall check limit. Unsupported/malformed recognized ICU structures can make checking unavailable rather than blocking editing.

The Issues panel also offers a cancellable local project check. It retains normal validation findings, adds spelling warnings/availability information, refuses stale workspace results and reports its 2,000-unit/finding limits as partial. It does not add synchronous dictionary work to Save or approval. No checking or suggestion request uses a remote service.

## Provenance and packaging

`third-party/WeCantSpell.Hunspell/7.0.1` contains the exact component record, license block, full MPL 1.1 text, notice and unmodified library source archive. The archive excludes upstream test dictionaries, benchmarks and icons. Version, package hash, source commit, source/notice hashes, chosen grant and net10.0 assembly hash are recorded. The selected net10.0 engine has no package runtime dependencies. This component decision does not classify dictionary data under the same license.

`tools/Verify-SpellingArtifacts.py` checks source/notices and the separate engine assembly, rejects dictionary files in release output and supports sidecar verification for a bundled application. The project copies source/notices outside single-file bundles. Windows publish/MSIX entry points and macOS packaging invoke the guard; CI publishes and checks folder output on its existing Windows/Linux/macOS matrix. Those CI jobs are configured, not claimed as executed here. Trimming/AOT and other runtime targets remain unverified.

Windows Release folder output, framework-dependent win-x64 single-file output and a self-contained win-x64 portable bundle were built and inspected for the component materials. The single-file engine identity is checked through the pinned package and separate-assembly folder build; the sidecar guard does not extract the bundled assembly. An installed Windows SDK 10.0.26100.0 produced an unsigned MSIX probe with a test identity and the existing Toucan logo. Its executable and all five component material files match the published inputs. This probe is not a production asset/signing/installation certification. Inno Setup was unavailable; native OS pickers, interactive packaged recovery/correction flows, signing, Linux/macOS execution, ReadyToRun/trimming/AOT and other architectures remain release-gate rows requiring evidence.

## Verification and acceptance

The final full solution run passes **1,384 tests**: Core 1,087, Avalonia 286 and plugin harness 11, with no failures or skips. The 42 added cases cover resource planning, local spelling and production UI/recovery workflows. Final UI captures and accessibility-label checks supplement the full suite.

| Gate | Recorded result |
|---|---|
| Full Windows solution / frozen plugin | 1,384 passed; no failures/skips; frozen archive hashes unchanged |
| Existing CLI compatibility | 19 corpus + six exact-resource + four fidelity cases passed |
| Framework/scanner integration | Passed, including 200 files / 10,000 references and a Windows junction cycle |
| Windows publishing | Folder, framework-dependent single-file and self-contained portable outputs built; materials/hashes verified |
| MSIX component probe | SDK package creation passed; exact executable/materials verified; unsigned and not installed |
| UI controls and layout | Production-composition/headless workflows, actual correction/metadata controls, light/dark captures |
| Native picker / installed package / signing | Unexecuted; release gate open |
| Inno installer | Tool unavailable; unexecuted |
| Linux/macOS / other architectures | CI configured; unexecuted in this session |
| ReadyToRun / trimming / AOT | Unverified; no release claim |

The final machine-readable report is [M7/UI verification](evidence/m7-ui-verification.json). It records full/focused suites, earlier failures, CLI results, screenshots, packaging checks, source fingerprints and frozen-plugin hashes. Initial runs exposed an oversized non-backtracking regex, ICU branch masking, target literal-path handling, missing localization and cancellation disposal; their failed results remain retained. A recovery test was corrected to establish its saved baseline through the lifecycle rather than writing the manifest behind that lifecycle; a YAML assertion was changed to check native-reopen semantics rather than assuming an unquoted locale key.

U1–U5 and M7a–M7b are accepted only for the bounded implementation and Windows production-composition/headless workflows recorded there. The original 19 corpus, six exact-resource and four fidelity CLI scenarios were rerun, along with the framework/cycle benchmark. M7c and the original M7 milestone remain open until the platform/native release rows above are completed. There is no parity percentage or guarantee of eliminating future legal claims.
