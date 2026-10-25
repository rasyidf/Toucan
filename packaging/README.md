---
title: "Toucan Packaging"
status: active
updated: 2026-10-25
summary: "Canonical solution, Windows packaging entry points, shared MSIX manifest, output layout, evidence retention and macOS signing."
---
# Toucan Packaging

## Supported entry points

Open `Toucan.CrossPlatform.slnx` for the maintained application, CLI, modules, SDK, samples and tests. The older `ToucanProject.slnx` was removed because it omitted maintained projects.

Run Windows packaging through one of these commands (Python 3.9+ and the .NET SDK from `global.json` are required):

```powershell
.\publish.ps1 portable
.\publish.ps1 installer
.\publish.ps1 msix
```

`portable` creates `dist/Toucan-<version>-portable-x64.zip`, containing the self-contained executable and required spelling-engine source/license sidecars. Distribute the complete ZIP; an EXE alone is incomplete.

`installer` needs Inno Setup 6 or 7 and creates `dist/ToucanSetup-<version>.exe`. Missing packaging tools cause failure rather than a successful-looking skipped package. `all` runs all three targets and requires both Inno Setup and the Windows SDK.

Windows staging folders live in ignored `build/publish/`; final Windows packages live in ignored `dist/`. App and installer versions come from `Directory.Build.props`.

## MSIX

Both `publish.ps1 msix` and `packaging/Build-Msix.ps1` use the same implementation and `packaging/msix/AppxManifest.xml`. The manifest keeps the `rasyid.dev.Toucan` identity and `.tproj` association from the former root publish path. The retired standalone manifest used a different identity (`rasyidf.Toucan`) and `.toucan` association; installations made with that experimental manifest need separate migration testing.

```powershell
.\packaging\Build-Msix.ps1
.\packaging\Build-Msix.ps1 -SkipBuild -LayoutOnly
.\packaging\Build-Msix.ps1 -Sign -CertPath path\to\cert.pfx
```

The Windows SDK supplies `makeappx.exe` and `signtool.exe`. `-LayoutOnly` prepares the layout without packing or signing; `-SkipBuild` reuses `build/publish/msix/`, which must contain the full published app and source/license materials. `-Configuration` defaults to `Release`; `-Version` optionally overrides the four-part package version.

`New-MsixAssets.ps1` generates correctly sized PNGs from the maintained app logo, including a 310×150 wide tile. Authored images in `packaging/msix/Assets/` override generated images. No 1×1 placeholders are generated. Asset generation requires Windows System.Drawing.

Unsigned output is for packaging inspection. Installation acceptance requires signing with a certificate trusted by the target machine; Developer Mode does not replace certificate trust. Signing, installation, upgrades and native workflow acceptance remain release gates.

## Build evidence and cleanup

Treat historical acceptance evidence as immutable. Before clearing `build/` or old `publish/` output, preserve reports, verification manifests, captures, fixture/probe outputs and referenced package artifacts outside the checkout. Record file hashes and verify the archive before removal. Restore archived paths under the repository root when reproducing a report that links to `build/`.

The 2026-10-25 local cleanup stores a Git bundle, evidence/package ZIP and per-file SHA-256 manifest in the sibling `Toucan-maintenance/2026-10-25/` directory. This is a local backup, not a published release or acceptance result. Do not commit regenerated outputs, archived binaries or local agent auto-state.

Keep the frozen M0 plugin fixture and third-party source archive tracked: they are required inputs, not accidental build products. Keep generated documentation HTML tracked for the existing static site; regenerate it from Markdown with `tools/build-docs.py` and `tools/generate-changelog.py`.

## macOS signing and notarization

`build-macos-app.sh` ad-hoc signs by default. macOS Gatekeeper blocks such a build on other Macs with "Apple could not verify Toucan is free of malware". To ship one that opens without a warning, you need an Apple Developer account and a Developer ID Application certificate:

```bash
xcrun notarytool store-credentials toucan-notary --apple-id you@example.com --team-id TEAMID --password <app-specific-password>
SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" NOTARY_PROFILE=toucan-notary packaging/build-macos-app.sh
```

Release the `.dmg` the script writes next to the app. It is the drag-to-Applications installer. Install `create-dmg` (`brew install create-dmg`) for a styled window; without it the script falls back to a plain `hdiutil` image.

## Spelling engine materials

The selected engine is WeCantSpell.Hunspell 7.0.1 under its MPL 1.1 alternative. Published output must retain `third-party/WeCantSpell.Hunspell/7.0.1`, including the license texts, notice, provenance and corresponding source archive. These sidecars remain outside single-file bundles; distributing an EXE alone omits required component materials. No dictionary pack is bundled.

Python 3.9+ runs `tools/Verify-SpellingArtifacts.py --folder <published-folder>` for a separate-assembly net10.0 build. Use `--bundled` for a source/notice sidecar audit of bundled output; it does not extract a bundled assembly or certify ReadyToRun/AOT transformations. Windows publishing/MSIX and macOS packaging invoke the guard. Keep generated artifact hashes with release evidence. CI checks folder output on its existing OS matrix.

See the [M7/UI implementation and remaining release rows](../docs/specs/resource-compatibility/m7-ui.md). Windows source/material packaging probes are not evidence of native picker behavior, installation/signing or Linux/macOS execution.
