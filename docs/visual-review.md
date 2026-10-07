---
title: "Visual review: screenshots of every screen"
status: active
updated: 2026-10-06
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

## What you get

| Files | Shows |
|-------|-------|
| `01`–`11` | Start screen, editor, every side panel, the multi-select bar, Zen mode, the unsaved title bar and the command palette |
| `10-options-*` | Every Settings page, plus the Plugins page filled with fixture plugins in each state |
| `20`–`28` | New project, Import, Project Properties (each page), Statistics, Manage languages, Provider settings and the small prompt dialogs |
| `30`–`38` | The dark theme: Settings, Project Properties, dialogs, the editor and every side panel |
| `40`–`42` | Settings search with a matching query, another matching query, and no matches |

The sample project has 60 generated keys so the pager and the Explorer tree have something to show.

## Notes

- Screenshots use the platform fonts and a 1280×800 window, so they are close to, not identical with, a real window. The macOS traffic-light inset is simulated with a fixed 78px.
- Popups (menus, flyouts) are separate windows and do not appear in these captures. Check them in the running app.
- Add a screen by adding a `Snap(window, "NN-name")` call where it is reachable. Keep new dialogs in `CaptureDialogs`.
