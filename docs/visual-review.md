---
title: "Visual review: screenshots of every screen"
status: active
updated: 2026-10-21
summary: "How to render PNGs of every screen headlessly with TOUCAN_TEST_SCREENSHOTS, what each file shows and the known limits."
---
# Visual review: screenshots of every screen

`tests/Toucan.Avalonia.Tests/UiScreenshotTests.cs` renders the real windows, side panels and dialogs headlessly (Avalonia.Headless with Skia) and saves a PNG of each. It is how UI changes get reviewed without clicking through the app, and it works on any OS (no UI Automation tooling needed).

## Run it

```bash
TOUCAN_TEST_SCREENSHOTS=/tmp/toucan-shots \
  dotnet run --project tests/Toucan.Avalonia.Tests -- -class Toucan.Avalonia.Tests.UiScreenshotTests
```

Without `TOUCAN_TEST_SCREENSHOTS` the tests do nothing, so a normal `dotnet test` stays fast and writes no files.

On Windows (PowerShell), build with a single MSBuild worker, then invoke the runner directly:

```powershell
dotnet restore tests/Toucan.Avalonia.Tests --disable-parallel -p:BuildInParallel=false -m:1
dotnet build tests/Toucan.Avalonia.Tests --no-restore -m:1
$env:TOUCAN_TEST_SCREENSHOTS = "$PWD/build/ui-review/after"
dotnet build/Toucan.Avalonia.Tests/Debug/net10.0/Toucan.Avalonia.Tests.dll -class Toucan.Avalonia.Tests.UiScreenshotTests
./tools/New-UiReviewGallery.ps1
```

Open `build/ui-review/after/index.html` to filter captures by name and theme. The two theme cases produce **140 PNGs**, in `light/` and `dark/`. The suite explicitly selects each theme and restores the previous theme afterward. UI tests run sequentially because they share application themes, command registries and side panels.

## What you get

| Files | Shows |
|-------|-------|
| `01`–`13` | Start screen, editor, every side panel, the multi-select bar, Zen mode, the unsaved title bar, the command palette, the shortcut sheet and ghost text |
| `10-options-*` | Every Settings page, plus the Plugins page filled with fixture plugins in each state |
| `20`–`28` | New project, Import, Project Properties (each page), Statistics, Manage languages, Provider settings and the small prompt dialogs |
| `29` | Onboarding, prompt editor, pre-translation and the plugin dialog host |
| `40`–`42` | Settings search with a matching query, another matching query, and no matches |
| `50` | Information, confirmation and unsaved-changes message overlays |

Every row is captured in **both** themes. Additional captures cover both new-project wizard steps, detected import files, minimum window sizes, Review/Audit modes and the File menu popup. `Snap` fails if rendering returns no bitmap; dialog captures also check that the resolved window surface is opaque. `DialogSurfaceTests` checks theme changes on an open dialog and verifies rendered client pixels with transparent Grid and Border roots.

The sample project has 60 generated keys so the pager and the Explorer tree have something to show.

## Notes

- Screenshots use the platform fonts and a 1280×800 window, so they are close to, not identical with, a real window. The macOS traffic-light inset is simulated with a fixed 78px.
- The File menu popup has a separate PNG; other context menus, flyouts, OS file pickers, native title bars, multi-monitor behavior and display scaling still need native desktop checks. Headless captures cannot prove Windows compositor correctness.
- Add a screen by adding a `Snap(window, "NN-name")` call where it is reachable. Keep new dialogs in `CaptureDialogs`.

See [the Windows review](windows-ui-review.md) for findings and remaining work.
