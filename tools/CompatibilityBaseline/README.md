# Compatibility baseline observer

Run from the Toucan repository root. Build with `dotnet build tools/CompatibilityBaseline/CompatibilityBaseline.csproj -m:1`, then execute `dotnet build/CompatibilityBaseline/Debug/net10.0/CompatibilityBaseline.dll .`.

The observer uses the normal Core/default module composition. Ten small, original inputs reproduce unresolved file compatibility questions. Inputs and saved outputs remain in a new timestamped directory under ignored `build/resource-compatibility/m0/probes/`; no user project, installed vendor resource or source fixture is edited. Unexpected exceptions make the observer return a failing exit code. Reproduced product bugs are observations, not assertions that the bug must persist.

Follow [the M0 acceptance report](../../docs/specs/resource-compatibility/acceptance.md) for the full test/CLI commands. After executing those commands, `python tools/CompatibilityBaseline/collect_evidence.py` validates and collects the results, inventories fixture hashes, and freezes sample plugin outputs. It requires three complete passing TRX suites, ten observations, 19 passing CLI cases and an unchanged source corpus. It does not fix product gaps or certify licenses.

The collector is specific to M0. Preserve the frozen artifacts and committed baseline; use another output directory for subsequent milestones.

M1 and M2 have separate evidence collectors, `collect_m1_evidence.py` and `collect_m2_evidence.py`. They consume their milestone's final complete TRX suites and verify the frozen M0 plugin archive without rebuilding it. M2 also consumes `tools/Test-ExactResourceMigration.py` results for the six exact-file formats. The observer's current package probes open projects through the registered project service; frozen M0 observations remain historical and must not be regenerated against later code.

M3 uses `collect_m3_evidence.py` and `tools/Test-ResourceFidelity.py` in addition to the existing CLI corpus/migration checks. Keep milestone outputs separate and preserve earlier evidence files.

M4 uses `collect_m4_evidence.py` for the complete solution suites and spreadsheet preview/undo/recovery evidence. It verifies the unchanged source corpus and frozen M0 plugin, records source hashes, and does not rerun or relabel earlier CLI evidence.

M5/M6 use `collect_m5_m6_evidence.py`, the full solution suites and `tools/Test-FrameworkIntegration.py` for generated-source permissions, profile workflows, source diagnostics/cycles and a measured original source corpus. Existing corpus, exact-resource and fidelity CLI checks are rerun; earlier evidence stays frozen.

M7/UI final evidence: run `python tools/CompatibilityBaseline/collect_m7_ui_evidence.py` after the full `m7-final-verification` suites, CLI regression scripts, UI captures and `Verify-SpellingArtifacts.py` publish checks. It retains earlier failed attempts and leaves M7c open for native/platform rows. The unsigned MSIX probe is a component-material check, not installation or production-asset acceptance. Prior milestone collectors and the frozen M0 archive must not be regenerated.
