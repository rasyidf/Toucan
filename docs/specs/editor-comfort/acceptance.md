---
title: "v0.24 acceptance evidence"
status: active
updated: 2026-10-25
summary: "Windows automated evidence for the 0.24 source build, completed editor workflows, reproducible commands and the remaining native/platform and external connector release gates."
---
# v0.24 acceptance evidence

The 0.24.0 source build implements the editor comfort work. It is not a published or fully accepted cross-platform release. Work was performed on `release/v0.23.0`, starting from compatibility commit `bf7d51e` and preserving the 18 pending UI/test edits. The initial verification preceded the commits and push requested afterward; no branch switch or release publication was performed.

## Implemented and verified behavior

- Bulk copy, move, Replace All, accepted pre-translation and approval are one-step undo actions. Entry history preserves review timestamps, comments, format metadata and resource identity. Delete already uses grouped structural history. Collisions and stale translation previews do not apply partial changes.
- A real headless keyboard test enters and approves 100 target values with `Ctrl/Cmd+Enter`, crosses page boundaries, checks retained field focus, saves and reopens all 100 approvals.
- Primary/source fields precede targets. Wide cards place source and target in two columns; narrow cards stack. Counts use Unicode scalar values and whitespace-delimited words. Arabic direction, narrow/wide layout, audit mode and generated-resource edit restrictions have regression coverage.
- Commands for accepting a suggestion, approving and advancing, and finding the next pending target use the existing shortcut registry. Translation-memory suggestions identify their origin.
- First-run setup stores default language and theme without requiring AI credentials. The guide can be dismissed and reopened from Help. Existing actionable welcome/empty states are retained.
- Notification history retains up to 200 host/plugin records, newest first, with severity, source, timestamp, clear and command actions. Save failure offers Retry save; validation offers Issues; failed commands offer Preferences. Running work retains its existing progress/cancel UI, with completed/cancelled/failed outcomes recorded in history.
- Toolbar contributions render actual registry commands, update on registration changes and respect workspace availability. This closes the missing desktop toolbar surface, with a rendered-button regression.

## Executed Windows checks

| Check | Result / evidence |
|---|---|
| Solution build | `dotnet build Toucan.CrossPlatform.slnx --no-restore -m:1` passes. A clean rebuild may emit the existing AVLN3001 runtime-loader warning for the injected MainWindow constructor. |
| Core suite | 1,087 passed, zero skipped. `build/v0.24-readiness/full-core-accepted.xml`. |
| Avalonia suite | 298 passed, zero skipped, including the 100-target keyboard session and shared notification composition. `build/v0.24-readiness/full-avalonia-accepted.xml`. |
| SDK harness | 11 passed, zero skipped. `build/v0.24-readiness/full-harness-accepted.xml`. |
| SDK packages / isolated consumer | Abstractions 1.2.0, Avalonia 1.0.0 and Testing 1.1.0 pack successfully. A copied in-memory sample builds using only the Abstractions NuGet package and loads in the CLI with one-run trust. `build/v0.24-readiness/package-consumer.json`. This is not the separate external connector's acceptance. |
| Babel fixtures | 19 projects passed; `python tools/Test-BabelParityFixtures.py`. |
| Exact-resource migration | Six formats passed; `python tools/Test-ExactResourceMigration.py`. |
| Resource fidelity | Four scenarios passed; `python tools/Test-ResourceFidelity.py`. |
| Framework/scanner | Generated-source protections and 200 files / 10,000 references passed, including dynamic/cycle diagnostics; `python tools/Test-FrameworkIntegration.py`. |
| Spelling source and published materials | Source/notice hashes and the separately published engine assembly passed `tools/Verify-SpellingArtifacts.py`; publish evidence in `build/v0.24-readiness/publish-materials.json`. No dictionary data is bundled. |
| UI captures | 193 fresh light/dark captures in `build/v0.24-readiness/ui`, including minimum window sizes, focused target, notification history, settings and onboarding. |

The standard VSTest bridge stalled without a result in this environment. Direct xUnit v3 execution succeeds. Set `TEMP` and `TMP` to a workspace-local directory, build first, then run each assembly directly, for example:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
$env:TEMP="$PWD/build/v0.24-readiness/temp"
$env:TMP=$env:TEMP
dotnet build Toucan.CrossPlatform.slnx --no-restore -m:1
dotnet build/Toucan.Core.Tests/Debug/net10.0/Toucan.Core.Tests.dll -xml build/v0.24-readiness/full-core-accepted.xml
dotnet build/Toucan.Avalonia.Tests/Debug/net10.0/Toucan.Avalonia.Tests.dll -xml build/v0.24-readiness/full-avalonia-accepted.xml
dotnet build/Toucan.Plugins.Testing.Tests/Debug/net10.0/Toucan.Plugins.Testing.Tests.dll -xml build/v0.24-readiness/full-harness-accepted.xml
```

The Core boundary test and framework scanner check create temporary Windows junctions. The restricted sandbox blocks `mklink`; both pass outside that sandbox. No test was disabled or weakened. Do not rebuild the Debug UI test output while its runner holds the assemblies open.

## Remaining release gates

| Gate | State and required evidence |
|---|---|
| Native Windows workflow/accessibility | Not executed: native desktop control is unavailable through the enabled tools. Run packaged-app editing, screen-reader announcements, OS text scaling, focus/caret/selection, dialogs and mixed RTL/LTR acceptance. Headless results are not native input evidence. |
| Linux/macOS | Not executed on this Windows host. Run supported-platform keyboard, layout, source/spelling and packaged-resource flows, and record exact OS/build/artifact hashes. |
| Compatibility M7c | Remains open until its native/platform rows are executed. Do not convert committed M3–M7 implementation into a claim of complete framework/platform parity. |
| External connector | The separate `toucan-plugins` repository is not present. Its package-only sample desktop/CLI gate remains open. Core connection activation exists, but no production desktop/CLI connection-management caller was found; adding that UI remains an inherited integration gap (APP-05). |

These gates are tracked in [known issues](../../known-bugs.md) and the [roadmap](../../todos/future-roadmap.md). They require another execution environment or the missing external project; they cannot be accepted from this host's test counts.

The final test and package hashes, application hash, base commit and SHA-256 of each changed working file are recorded in `build/v0.24-readiness/verification-manifest.json`. That manifest records the pre-commit editor snapshot. The editor, shared dialog chrome and contextual start-screen/menu follow-ups are committed together; Git history records the final revision.

## Contextual shell follow-up

Shared dialog chrome and project-aware menu/palette visibility were added after the initial editor snapshot. The start-screen activity rails now collapse with their columns and return when a project opens. Shell tests cover opening and closing projects in light/dark themes at normal and minimum window sizes. These follow-ups retain the native/platform gates above.

## Upstream merge verification

On 2026-10-25, the documentation commit `2c2c7f6` and local editor/compatibility changes were reconciled with upstream `6204332`, preserving both histories. The merged implementation includes placeholder rename and warnings, inspector context, the feature tour and accessibility improvements alongside the local shell and metadata-preserving undo workflows.

Every project listed in `Toucan.CrossPlatform.slnx` builds individually with `--no-restore -m:1 -c Review`; the solution itself only defines its default configurations. Direct xUnit execution passed 1,087 Core tests, 337 UI tests and 11 SDK harness tests (1,435 passed total). The UI suite's external connector test was skipped because `TOUCAN_EXTERNAL_CONNECTOR_DIST` is not configured. Evidence: `build/v0.24-readiness/merged-full-core.xml`, `merged-full-ui.xml` and `merged-full-harness.xml`. Documentation status validation also passes. These checks retain all remaining native/platform and external connector release gates above.
