# Compatibility baseline observer

Run from the Toucan repository root. Build with `dotnet build tools/CompatibilityBaseline/CompatibilityBaseline.csproj -m:1`, then execute `dotnet build/CompatibilityBaseline/Debug/net10.0/CompatibilityBaseline.dll .`.

The observer uses the normal Core/default module composition. Ten small, original inputs reproduce unresolved file compatibility questions. Inputs and saved outputs remain in a new timestamped directory under ignored `build/resource-compatibility/m0/probes/`; no user project, installed vendor resource or source fixture is edited. Unexpected exceptions make the observer return a failing exit code. Reproduced product bugs are observations, not assertions that the bug must persist.

Follow [the M0 acceptance report](../../docs/specs/resource-compatibility/acceptance.md) for the full test/CLI commands. After executing those commands, `python tools/CompatibilityBaseline/collect_evidence.py` validates and collects the results, inventories fixture hashes, and freezes sample plugin outputs. It requires three complete passing TRX suites, ten observations, 19 passing CLI cases and an unchanged source corpus. It does not fix product gaps or certify licenses.

The collector is specific to M0. Preserve the frozen artifacts and committed baseline; use another output directory for subsequent milestones.

M1 and M2 have separate evidence collectors, `collect_m1_evidence.py` and `collect_m2_evidence.py`. They consume their milestone's final complete TRX suites and verify the frozen M0 plugin archive without rebuilding it. M2 also consumes `tools/Test-ExactResourceMigration.py` results for the six exact-file formats. The observer's current package probes open projects through the registered project service; frozen M0 observations remain historical and must not be regenerated against later code.
