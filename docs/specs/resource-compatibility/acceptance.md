---
title: "Resource compatibility: M0 acceptance baseline"
status: active
updated: 2026-10-22
summary: "M0 evidence at ea3d255: 1,139 passing tests, 19 CLI fixture checks, ten isolated probes, current gap dispositions and versioned acceptance scope."
---

# Resource compatibility: M0 acceptance baseline

Matrix version: **1**. Baseline: **ea3d255b6acea0e202c2c2455bdb3317b06e25ce**, `docs(changelog): record Windows UI and BabelEdit compatibility fixes`. Execution date: **2026-10-22, Asia/Jakarta**. Platform: Windows x64; .NET SDK **10.0.401**; test framework: xUnit v3 with VSTest. The working tree was clean before this audit. Production code and pre-existing fixtures were not changed.

M0 is accepted as a baseline investigation. It does not close M1–M7, certify complete BabelEdit compatibility, or approve any vendor-resource redistribution. The [implementation plan](tasks.md) and [resource boundaries](../../research/babeledit-resource-integration.md#product-direction-and-compatibility-boundary) remain authoritative.

Subsequent work is recorded separately: [M1 contracts and identity](m1.md). M0 counts, observations and hashes below remain the original baseline.

## Executed evidence

The checked-in [machine-readable baseline](evidence/m0-baseline.json) contains test counters by class, TRX hashes, all ten observations, fixture/file hashes, CLI case results and sample-plugin artifact hashes. Generated before/after projects, test binaries and TRX files remain under ignored `build/`; their exact paths are recorded in that baseline. Reproduce the observations with [CompatibilityBaseline](../../../tools/CompatibilityBaseline/Program.cs) and collect the evidence with [collect_evidence.py](../../../tools/CompatibilityBaseline/collect_evidence.py).

| Execution | Result | Scope |
|---|---|---|
| `dotnet test Toucan.CrossPlatform.slnx --no-restore -m:1 --logger 'trx;LogFilePrefix=m0-complete' --results-directory build/resource-compatibility/m0/test-results --verbosity minimal` | **1,139 passed; 0 failed; 0 skipped** | Avalonia 245; Core 883; plugin testing harness 11 |
| `dotnet build Toucan.CLI/Toucan.CLI.csproj --no-restore -m:1 --verbosity minimal` | Pass; zero warnings/errors | Builds the current CLI before running its audit |
| `python tools/Test-BabelParityFixtures.py` | **19/19** cases pass recorded checks | Key count, Unicode, greeting, no keys added/lost and unchanged Unicode after an isolated CLI save/reload |
| `dotnet build tools/CompatibilityBaseline/CompatibilityBaseline.csproj --no-restore -m:1 --verbosity quiet` | Pass; zero warnings/errors | Builds independent observation tool |
| `dotnet build/CompatibilityBaseline/Debug/net10.0/CompatibilityBaseline.dll .` | **10/10 probes executed without unexpected exceptions** | Reproduces known gaps; completion is not a product pass |
| `python tools/CompatibilityBaseline/collect_evidence.py` | Pass | Requires three passing suites, ten completed probes, 19 passing CLI cases and unchanged corpus; freezes sample plugin outputs and hashes |

The full run includes 48 `BabelParityRegressionTests`, five `BabelExcelTests`, two headless `BabelImportFlowTests`, 208 `FormatFidelityFixtureTests`, 11 `FormatSupportTests`, 21 `ProjectTextFormatTests`, and 16 `SaveRecoveryTests`. These are subsets of 1,139, not additional tests. The two GUI workflow tests exercise metadata-only spreadsheet changes and custom JSON paths, save/reopen and Save As through the headless Avalonia host; native window/picker operation was not tested.

The initial sandboxed VSTest run aborted because the test host could not connect after 90 seconds. An initial unrestricted `--no-restore` run executed only the two previously restored test projects. All solution projects were then restored from the local NuGet cache and the final `m0-complete` run executed all three test assemblies successfully outside the restricted sandbox. Only the final three suites are counted. No product failure was inferred from the runner issue.

## Historical risk dispositions

The IDs below correspond to the eight numbered source-level risks in the research report. Fixed means the stated tested subset is fixed; it does not certify every variation of the broader feature.

| ID | Disposition | Executed observation / evidence | Closure owner |
|---|---|---|---|
| G1: non-JSON explicit paths and languages | **Open** | `G1-nonjson-explicit-path`: a YAML file explicitly declared as `en` loads as `english`; unrelated sibling `de.yml` is included; warnings empty | M2: exact-file dispatch and declared language |
| G2: package IO hard-coded to JSON / wrong destination | **Open** | `G2-package-yaml-load` returns no entries for a valid YAML package. `G2-package-json-save` leaves `custom/english.json` at `Before` and creates `custom/en.json` | M1–M2: file IO contract and package routing |
| G3: flattened key identity collisions | **Open** | `G3-literal-nested-collision`: literal `a.b` and nested `a`/`b` both load as `a.b`; direct JSON save retains only nested `Nested`, losing `Literal` | M1–M3: structured identity and document state |
| G4: cached imported key breaks rename | **Fixed with evidence for existing JSON paths** | `NativeBabelImportRetainsMetadataCustomPathsAndKeyRenames`, `NativeJsonPackagesKeepIdenticalKeysInSeparateFiles` and GUI custom-path save tests pass | Retain fixes; M1/M3 still need package moves, duplicate, delete, undo and recovery identity tests |
| G5: retained settings not fully applied | **Open** | `G5-imported-settings`: imported `indent=none` and `translation_order=file` are retained, but saved JSON is indented and keys sorted `a,z` instead of input `z,a`. `support_arrays=false` retention is observed; its behavior is not certified by this probe | M1/M3 typed options and per-resource precedence; M5 FormatJS dialect |
| G6: editable ARB metadata disconnected | **Open** | `G6-arb-description-edit`: initial comment is empty despite a description; setting the comment to `New description` leaves `Old description` in `@title`. Unknown `custom` metadata survives | M3 metadata mapping and merge |
| G7: spreadsheet package/approval loss | **Fixed with evidence for existing schema** | All five `BabelExcelTests` pass, including mixed approvals/packages, legacy Toucan workbook and synthetic native BabelEdit export. Headless metadata-only GUI edit survives reopen | M4 retains fixes; dialect separation, neutral identity, preview/undo, omitted-cell semantics and text safety still require coverage |
| G8: JSON null loss and encoding detection | **Open for nulls; encoding subcase fixed with evidence** | `G8-json-null`: `nil:null` disappears on direct JSON save. Java UTF-8/Latin-1, explicit ambiguous-byte encoding and newline conversion regressions pass | M3 preserves nulls and retains encoding fixes; unsupported overrides/resource-specific options remain future cases |

Additional executed boundaries:

| ID | Disposition | Evidence / required behavior |
|---|---|---|
| S1: unknown project schema version | **Open** | `S1-unknown-project-version`: `version=999.0` imports one entry without warnings. Acceptance scope below excludes unknown versions even though current code accepts them. M2 must add schema validation |
| S2: FormatJS extraction framework | **Unsupported with diagnostic** | `S2-unsupported-formatjs` throws `FormatException` naming `react-formatjs-extract` as unsupported. Preserve this explicit boundary until M5 implements and tests a dialect |
| S3: generic YAML root mistaken for Rails | **Open** | `S3-generic-yaml-locale-root`: declared generic YAML with an `en` group loads `title` rather than `en.title` and sets `yaml.locale=en`. M3 must make wrapper semantics dialect-aware |

No remaining historical risk is classified solely from source inspection, and no reproduced gap is hidden by the passing broad regression suite. Reproductions use copies or newly generated temporary inputs and retain their outputs for inspection. Direct strategy probes establish behavior of those API paths; they do not assert the GUI bypasses format save guards.

## Acceptance matrix v1: supported evidence subset

**Baseline pass** below means only the fixture's loaded translation values survive native save/reopen, plus the CLI checks stated above. It does not mean lossless preservation of arbitrary metadata/layout or full integration with the named framework. All 19 corpus projects declare schema **1.3** and two languages, English/German. Original `.babel` bytes remain unchanged in `CorpusSurvivesNativeSaveAndReopen`.

| Fixture | Schema / framework | Resource dialect | Verified workflow | Status / limit |
|---|---|---|---|---|
| `01-generic-json` | 1.3 / generic-json | Flat dotted JSON | Core import/save/reopen + CLI mutation | Baseline pass; literal/nested collision is G3 |
| `02-angular-ngx` | 1.3 / ngx-translate | Flat JSON | Same | Baseline pass; source extraction unverified |
| `03-angular-transloco` | 1.3 / angular-transloco | Nested JSON | Same | Baseline pass; scope/directive extraction unverified |
| `04-vue` | 1.3 / vue-json | Nested JSON | Same | Baseline pass; SFC resources/scanning unverified |
| `05-i18next` | 1.3 / i18next | Nested JSON, one file per language | Same | Baseline pass; directory namespaces and dynamic/fallback extraction unverified |
| `06-react` | 1.3 / react-json | Flat JSON | Same | Baseline pass; generated FormatJS catalogs unsupported (S2) |
| `07-svelte` | 1.3 / svelte-i18n | Flat JSON | Same | Baseline pass; source integration unverified |
| `08-ember-json` | 1.3 / ember-json | Nested JSON | Same | Baseline pass; source integration unverified |
| `09-laravel-json` | 1.3 / laravel-json | Plaintext JSON keys | Same | Baseline pass; literal separator ambiguity remains G3 |
| `10-generic-yaml` | 1.3 / generic-yaml | YAML scalar map | Same | Baseline pass; locale-looking generic roots fail S3 |
| `11-ember-yaml` | 1.3 / ember-yaml | YAML scalar map | Same | Baseline pass; arbitrary path/language mapping fails G1 |
| `12-rails-yaml` | 1.3 / rails-yaml | Locale-wrapped YAML | Same | Baseline pass; generic/exact wrapper boundaries remain M3 |
| `13-laravel-php` | 1.3 / laravel | Literal returned PHP array | Same | Baseline pass for this subset; arbitrary code/comments not certified |
| `14-flutter-arb` | 1.3 / arb | ARB strings and opaque metadata | Same | Baseline pass for loaded values; description editing fails G6 |
| `15-java-latin1` | 1.3 / prop-files | ISO-8859-1 / Unicode escapes | Same | Baseline pass; explicit ambiguous-byte regression also passes |
| `16-java-utf8` | 1.3 / prop-files | UTF-8 properties | Same | Baseline pass; Unicode and selected encoding regressions pass |
| `17-json-arrays` | 1.3 / generic-json | Nested JSON arrays/scalars/null | Same, loaded values only | Baseline pass excludes null fidelity; G8 confirms null loss |
| `18-yaml-arrays` | 1.3 / generic-yaml | YAML sequences | Same | Baseline pass; additional Rails sequence-of-mapping regression passes |
| `19-dotnet-resx` | 1.3 / resx | RESX strings | Same | Baseline pass; arbitrary metadata/non-string resources not certified |

Cross-cutting schema/workflow cases:

| Variant | Evidence | Acceptance boundary |
|---|---|---|
| Minimal `.babel` without schema version | Inline existing JSON path/rename fixtures | Tested permissive legacy input; not evidence of any other declared schema version |
| 1.3 generated `.babel` with custom basenames | M0 probes G1–G2 and existing JSON custom-path tests | JSON path case passes; non-JSON explicit language/path case fails |
| Multiple JSON packages with equal IDs | Core regression + synthetic spreadsheet round trip | Pass for tested package-prefix representation; structured identity still M1 |
| Old native project `saveStyle` numeric/name fields | 49 `ProjectSettingsFormatTests`, including legacy migration | Preserve existing read migration and native format ID serialization |
| Current native project text preferences | 21 `ProjectTextFormatTests` | Tested encoding/newline paths pass; not a complete per-resource settings model |
| BabelEdit-style XLSX | Five exchange tests + GUI metadata-only test | Tested package/ID and per-language approval schema passes |
| Legacy Toucan XLSX | `LegacyToucanWorkbookStillImports` | Old shared-approval import retained; export dialect choice remains M4 |
| Unsupported framework / external path | S2 + `BabelImportRejectsEscapingResourcePath` | Explicit rejection; symlink/reparse-point handling not certified |
| Unknown schema versions | S1 | Excluded from supported claims; current missing diagnostic is open |
| XLIFF, PO/POT `.babel` workflows | No dedicated app-to-app corpus case in this audit | Generic format tests do not certify migration workflows; unverified |
| Multilingual beyond two languages, RTL, huge catalogs, arbitrary external paths | No dedicated cases in this audit | Unverified; no implied certification |

## Fixtures and provenance

The corpus README and generator identify original synthetic translations, not customer data. `01-generic-json/project.babel` is user-created output of BabelEdit 5.5.1; 18 variants were generated from that seed. The XLSX is user-generated export of synthetic content. These are interchange data, not installed vendor implementation/demo assets. Their hashes and each project’s declared schema/configuration are recorded in the evidence JSON. Source generation does not establish native BabelEdit behavior for every variant.

The M0 probe tool independently constructs small inputs using `XElement` and original strings. It reads no BabelEdit install directory and copies no proprietary resources. Existing inline xUnit fixtures exercise encoding, approval, path, key rename, recovery and legacy projects. The checked-in machine-readable results contain only generated observations, counts and hashes. The original corpus still matches baseline HEAD after execution.

Third-party engine/dictionary artifacts are absent from this M0 change. A provenance review for future language packs remains M7; this inventory is not blanket legal clearance of the repository.

## Plugin and native-project baseline

All plugin contract/host/activation/services/trust/workspace planner tests pass. In particular: `PluginContractTests` 28, `PluginCompatibilityTests` 8, `PluginHostTests` 35, `SamplePluginTests` 10, headless `DesktopPluginTests` 11, `PluginsUiTests` 16 and the connector harness 11. Sample tests install real compiled plugin output, register formats/rules, create and save a TSV project, and reopen it through the host.

The baseline sample plugin DLL, desktop DLL and manifest were copied unchanged to `build/resource-compatibility/m0/frozen-plugin/`; hashes are committed in the evidence JSON. Preserve these binaries for M1 to load against changed contracts. They were built from baseline source, before any contract changes. They are **not** a separately obtained historical published plugin, so cross-release binary compatibility remains unverified until M1 tests such an artifact. Do not claim that the current compatibility-version unit tests prove that broader ABI promise.

Native numeric/name `saveStyle` migrations, text preferences and 16 save/recovery cases pass. Keep those suites and frozen plugin tests as regression gates before changing contracts. No old native projects were rewritten outside temporary test directories.

## Named closure cases for subsequent milestones

These IDs are planned acceptance cases, not newly passing tests. Prefer extending existing cases where equivalent; the M0 observer records behavior without asserting that bugs must remain.

| Milestone | Required case IDs / fixtures |
|---|---|
| M1 | `identity.literal-vs-nested` (G3); `identity.array-vs-numeric-object`; `identity.same-key-packages`; `identity.rename-move-undo-recovery`; `document.empty-state`; `plugins.frozen-baseline-load`; `native.legacy-migration` |
| M2 | `migration.yaml-explicit-language-siblings` (G1); `package.yaml-exact-read` and `package.custom-filename-write` (G2); `migration.schema-unknown` (S1); `migration.missing-resource`; `migration.duplicate-mapping`; `migration.path-symlink-boundary`; `save.multifile-failure-recovery` |
| M3 | `settings.indent-order-precedence` (G5); `settings.array-policy`; `json.null-preservation` (G8); `arb.description-placeholder-edit` (G6); `yaml.generic-root-vs-rails` (S3); retain `DeclaredJavaEncodingWinsOverAmbiguousUtf8Bytes` and current rename/empty/encoding regressions |
| M4 | Retain `RoundTripPreservesMixedApprovalsAndPackages` and legacy/schema rejection tests; add `exchange.dialect-choice`, `exchange.omitted-vs-cleared`, `exchange.preview-undo`, `exchange.formula-text`, `exchange.size-limit` |
| M5 | `framework.old-profile-defaults`; `framework.generated-source-restrictions-ui-cli`; `formatjs.catalog-metadata` (S2); `settings.unknown-option-diagnostic` |
| M6 | `scanner.i18next-namespaces`; `scanner.transloco-directives`; `scanner.vue-template`; `scanner.rails-symbols`; `scanner.formatjs-jsx`; `scanner.multiline-dynamic`; `scanner.cancel-incomplete-no-cleanup` |
| M7 | `spelling.locale-aliases`; `spelling.placeholder-icu-exclusion`; `spelling.stale-results-correction-undo`; `dictionary.path-archive-limits`; `release.component-provenance`; `release.no-vendor-resource-dependency` |

## Reproduction and validation notes

Run from the repository root in PowerShell. `MSBuildEnableWorkloadResolver=false` avoids this host's workload Service Control Manager permission error; no workloads are installed. Restore every test project's assets before using `--no-restore`, since an unrestored project can otherwise be silently omitted by `dotnet test`.

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore Toucan.CrossPlatform.slnx --source "$env:USERPROFILE\.nuget\packages" --ignore-failed-sources -p:NuGetAudit=false -m:1 --verbosity minimal
dotnet test Toucan.CrossPlatform.slnx --no-restore -m:1 --logger 'trx;LogFilePrefix=m0-complete' --results-directory build/resource-compatibility/m0/test-results --verbosity minimal
dotnet build Toucan.CLI/Toucan.CLI.csproj --no-restore -m:1 --verbosity minimal
python tools/Test-BabelParityFixtures.py
dotnet restore tools/CompatibilityBaseline/CompatibilityBaseline.csproj --source "$env:USERPROFILE\.nuget\packages" --ignore-failed-sources -p:NuGetAudit=false -m:1 --verbosity minimal
dotnet build tools/CompatibilityBaseline/CompatibilityBaseline.csproj --no-restore -m:1 --verbosity quiet
dotnet build/CompatibilityBaseline/Debug/net10.0/CompatibilityBaseline.dll .
python tools/CompatibilityBaseline/collect_evidence.py
```

The local-cache source and disabled NuGet audit were used to make this execution independent of network availability; dependency vulnerability auditing was not performed. A machine without the cached packages should restore from its configured approved package sources. The collector pins the current baseline and refuses changed frozen artifacts; a later implementation baseline must use a separate evidence location rather than overwriting M0.

Repository documentation validation initially reported two existing defects: `docs/babeledit-parity-review.md` lacks a YAML header and `docs/formats.md` lacks `updated`. Neither was introduced or repaired by M0. Changed documentation headers/links and whitespace are checked separately; those baseline documentation defects do not block the runtime baseline.
