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

## Deliberately not ported

These exist in WPF but do nothing there, so porting them would add dead UI:

- **Backdrop type** setting: WPF saves it but nothing applies it.
- **Pin recent project**: toggles a flag in memory; it is never saved or used.
- **Check for updates / update channel**: a UI shell with no update check behind it. The auto-updater is v0.24 (FG-02).
- **Dictionary** and **Source Control** panels: "coming soon" placeholders in WPF. They return with the glossary (v0.21) and Git (v0.23) work.
- **Open log location**: opens the program folder, not a log folder.

## Not translated yet

Messages built at run time (status text, confirmation prompts, validation messages) are still English. Menus, panels,
dialogs, tooltips, pagination and the status bar are translated.

## What retiring WPF still involves

Nothing blocks it in the product. The remaining steps are repository changes: remove `Toucan/` and its tests
(`tests/Toucan.Tests`) from `ToucanProject.slnx`, drop `Toucan.CrossPlatform.slnx`'s "except WPF" caveat, and delete the
folders.
