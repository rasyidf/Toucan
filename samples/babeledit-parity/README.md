# BabelEdit interoperability fixtures

Synthetic English/German translations, created on Windows on 2026-10-21.
No customer translation files are included.

`01-generic-json/project.babel` was created and saved through BabelEdit 5.5.1.
The other projects were generated from that seed by
`python tools/New-BabelParityFixtures.py`. Generation is not proof of BabelEdit
compatibility: the comparison report identifies the projects actually opened in
the app. The generator preserves existing files so it cannot undo subsequent UI
edits.

Open any `project.babel` in BabelEdit. Open the containing directory in Toucan;
when no native `.tproj` exists and exactly one `.babel` is present, Toucan imports
its settings and review metadata. Save creates a native `.tproj` while leaving
the `.babel` file unchanged.

Run the reproducible Toucan audit after building Toucan.CLI:

```powershell
dotnet restore Toucan.CLI/Toucan.CLI.csproj --disable-parallel -m:1
dotnet build Toucan.CLI/Toucan.CLI.csproj --no-restore -m:1
python tools/Test-BabelParityFixtures.py
```

The audit reads all projects, then saves only isolated copies under
`build/babeledit-parity/roundtrip-*`. Original fixtures are untouched. It uses an
empty plugin location, never calls translation providers, and records raw CLI
outputs in `build/babeledit-parity/toucan-read-results.json`.

The CLI checks cover discovery, Unicode, placeholders in the greeting, and key
loss after a CLI save. Core regression tests additionally compare every loaded
value in all 19 fixtures across save/reopen. GUI tests cover Excel metadata and
custom-path import/save/reopen/Save As. Broader source-scanning, framework and
live-provider parity remains unverified. `babeledit-export.xlsx` is the original
synthetic BabelEdit export used by the Excel regression tests.

See [the comparison](../../docs/babeledit-parity-review.md) for findings,
evidence, limitations, and release criteria.
