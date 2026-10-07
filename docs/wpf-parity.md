> The WPF app was removed from `main` after v0.17.3 and lives on the `legacy/wpf` branch. This record stays as history.

# WPF → Avalonia parity

The Avalonia app (`Toucan.Avalonia`) is the supported desktop app on Windows, macOS and Linux. The WPF app (`Toucan`) is
deprecated: it receives no new features and no longer builds against the current `Toucan.Core`. This page records the
audit that justified retiring it. The audit compared commands, settings, dialogs, panels, menus, shortcuts, services and
localization, in code, not only in the UI.

## Equivalent or better in Avalonia

| Area | WPF | Avalonia |
|------|-----|----------|
| Menus and commands | File/Edit/Find/Views/Tools/Help | Same set, plus Review/Audit modes, Search & Replace, bulk edits, plugins |
| Side panels | Explorer, Search, Issues, Source Code, Languages, Inspector, Translation, Memory | Same, plus the Translation panel (provider picker built from the registry, so plugin providers appear) |
| Settings | 12 pages | Same pages, plus Integration and Plugins |
| Dialogs | New project, import, language prompt, manage languages, pre-translate, project properties, project defaults, provider settings, statistics, about | Same; project defaults and about live in Settings |
| Language filter | "Focused languages" text prompt | Per-language visibility checklist |
| File association | Windows registry (Settings → Integration) | Windows registry, Linux `.desktop` + MIME, macOS bundle declaration and open-file events |
| Shortcuts | Copy template 1–3 | Copy template 1–5, macOS-native menu shortcuts |
| Localization | `Strings.resx` (47 keys, 22 used) | Every menu, panel, dialog and settings label (`Locales/Strings.id-ID.json`); a test fails when a UI string lacks a translation |

## Ported as part of retiring WPF

Localization, `.tproj` file association, the Machine Translation panel, Trim Line by Line, Cut Key Values, and copy-template
shortcuts 4 and 5 (WPF stopped at 3).

**Pin recent project** is ported and finished. In WPF the pin toggle only flipped an in-memory flag that was never saved or
used. In the Avalonia app a pin is stored in `recent_projects.json`, pinned projects sort first on the Start screen and in
Open Recent, and they do not count toward the list limit. Settings → General → Recent projects adds the list limit, a
"keep pinned projects when clearing" switch, a manage list (pin, unpin, remove), and a switch that detects the preferred
language from the most recently opened project (it records each project's source language when it opens).

## Deliberately not ported

These exist in WPF but do nothing there, so porting them would add dead UI:

- **Backdrop type** setting: WPF saves it but nothing applies it.
- **Check for updates / update channel**: a UI shell with no update check behind it. The auto-updater is v0.24 (FG-02).
- **Dictionary** and **Source Control** panels: "coming soon" placeholders in WPF. They return with the glossary (v0.21) and Git (v0.23) work.
- **Open log location**: opens the program folder, not a log folder.

## Not translated yet

Messages built at run time (status text, confirmation prompts, validation messages) are still English. Menus, panels,
dialogs, tooltips, pagination and the status bar are translated.

## Retiring WPF

Done. The WPF app and its tests were removed from `main`; the source is on the `legacy/wpf` branch.
