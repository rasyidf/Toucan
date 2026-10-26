---
title: Build and runtime memory
status: active
updated: 2026-10-26
summary: "v0.24 measurements for Linux ARM64 compilation and packaged-app memory; full-suite VM limits and unmeasured workloads are recorded separately."
---
# Build and runtime memory

These are observations from the v0.24.0 platform verification, not certified minimum system requirements. Compilation, automated UI tests and normal app use have different memory costs.

## Observed memory

Source: `28336f1`, Linux ARM64, .NET SDK 10.0.403, Ubuntu 24.04.5 in a Colima VM with 2 vCPUs and 2 GiB configured RAM. The final checks and memory measurements ran with 2 GiB temporary swap enabled. Dependencies had already been restored.

| Workload | Observed memory | Outcome |
|---|---|---|
| Full Release rebuild of the maintained solution, one worker, shared compiler disabled | **994 MiB peak aggregate process-tree RSS** | Completed in 82.9 seconds; includes application, CLI, modules, samples and test-project compilation |
| Packaged app at the start screen | **241 MiB app-process peak and settled RSS** | Native Linux GUI remained running for the 20-second observation |
| Packaged app with 100 keys and two languages | **283 MiB app-process peak; 278 MiB settled RSS** | Native Linux GUI loaded a synthetic flat-JSON project and remained running for 20 seconds |
| Complete automated test suite | Peak RSS was not sampled | **1,441 passed, one optional external-connector test skipped**, using the 2 GiB VM plus 2 GiB swap |

The start-screen and project measurements exclude the Xvfb display server. Including that server and its launcher, the corresponding process-tree peaks were 262 MiB and 304 MiB. The project contained 100 keys in each of `en-US.json` and `id-ID.json`; onboarding and the feature tour had already been completed. No dictionary packs or third-party plugins were installed, and no translation requests or editing actions were performed during the observation.

The whole solution compiled earlier in the 2 GiB VM before swap was added. Full UI-suite attempts in that VM without swap were killed by the Linux out-of-memory handler. Diagnostic processes were also resident during those attempts, so this does **not** establish a clean minimum-RAM threshold for the tests. The later successful full run used normal .NET GC settings and temporary swap; an experimental 512 MiB per-process heap cap was too restrictive and was removed.

## What to budget for

For the measured small project, the application process used about 0.3 GiB. A full sequential build reached about 1 GiB across the build process and its children. Both figures exclude the operating system, desktop, editor/IDE and other applications; they are not total-machine RAM requirements. The UI test suite needs a separate, larger budget than ordinary app use.

Larger projects, long editing sessions, dictionaries, plugins, machine translation and different graphics drivers still need measurements. macOS and Windows RAM requirements have not been profiled. macOS tests and both Apple silicon and Intel app/DMG builds passed, but those results do not supply a macOS memory minimum.

## Reproduce the measurements

The Linux-only sampler reads `/proc` every 100 ms. Aggregate process-tree RSS can count shared pages more than once; it is not unique physical-memory usage. Runtime RSS is recorded for the `Toucan` process alone. Settled runtime RSS is the median of the final five seconds. A short startup spike between samples may be missed.

Restore dependencies before measuring compilation:

```bash
dotnet restore Toucan.CrossPlatform.slnx
python3 tools/Measure-ProcessMemory.py \
  --output build/build-memory.json --log build/build-memory.log -- \
  dotnet build Toucan.CrossPlatform.slnx -c Release -t:Rebuild \
  --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
```

For a self-contained Linux publish, use the appropriate runtime identifier, then run the existing component-material guard:

```bash
dotnet publish Toucan.Avalonia/Toucan.Avalonia.csproj -c Release \
  -r linux-arm64 --self-contained -m:1 -o build/runtime-memory-app
python3 tools/Verify-SpellingArtifacts.py --folder build/runtime-memory-app
```

Complete first-run setup before sampling. Xvfb and the app's native X11/font libraries must be installed for this virtual-display measurement:

```bash
python3 tools/Measure-ProcessMemory.py \
  --output build/runtime-memory.json --log build/runtime-memory.log \
  --duration 20 --primary-name Toucan -- \
  xvfb-run -a build/runtime-memory-app/Toucan
```

Append a project folder to the app command to measure a loaded project. The sampler deliberately terminates the process group after the observation; `survivedObservation: true` with exit code `-15` records this planned stop, not a startup failure. This benchmark checks startup and a brief idle period, not a sustained editing workload.

The numeric records, test counters and published spelling-material hashes are retained in [the v0.24 evidence](evidence/v0.24-platform-memory.json). Temporary swap and the disposable verification container are removed after the checks; swap is not part of Toucan's installation.
