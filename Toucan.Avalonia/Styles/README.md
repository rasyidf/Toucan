# Shared styles

`App.axaml` configures FluentAvalonia, registers converters, and loads resources
and `AppStyles.axaml`. Keep shared visual definitions in this folder.

## Resources

| File | Owns |
| --- | --- |
| `Resources/Tokens.axaml` | Editor font size and settings layout dimensions |
| `Resources/Light.axaml` | Default light palette |
| `Resources/Dark.axaml` | Default dark palette |

Keep the same brush keys in both palettes. The application retains its top-level
Light and Dark theme dictionaries because `ColorSchemeService` reads defaults
from them and writes user overrides there. `ThemeService` similarly overrides
`EditorFontSize` at application scope. Included resources supply the defaults.

## Style modules

`AppStyles.axaml` is the ordered entry point. Its includes preserve the original
cascade; moving a rule between files can change precedence.

| Module | Owns |
| --- | --- |
| `Typography.axaml` | Text roles and monospace text |
| `Buttons.axaml` | Shared icon, link, option, and row buttons |
| `Shell.axaml` | Activity navigation, window chrome, surfaces, badges, status bar, plain lists |
| `Editor.axaml` | Translation cards, fields, selection, pagination |
| `Dialogs.axaml` | Dialog footers and form labels |
| `Settings.axaml` | Settings groups, rows, lists, tables, navigation, color controls |
| `Inputs.axaml` | Tabs, input defaults, status dots, disclosure toggles |
| `ProjectTiles.axaml` | Framework and statistics tiles |
| `Panels.axaml` | Inspector rows, panel density, provider rows |
| `Toolbars.axaml` | Toolbar controls and bulk actions |
| `Overlays.axaml` | Command palette, title-bar command entry, save state |
| `Compatibility.axaml` | Existing late overrides awaiting consolidation |

## Gradual migration

1. Put new shared rules in their owning module. Keep screen-specific layout in
   the view until it has a reusable role.
2. When consolidating an override from `Compatibility.axaml`, check specificity
   and load order, then remove the old rule. Do not add new rules to that file.
3. Extract repeated dimensions into named tokens as each component is migrated.
   Preserve existing resource keys used by services, plugins, and views.
4. Keep structural moves separate from changes to colors, density, or behavior.
5. Build and run the appearance, accessibility, and component tests. Generate
   screenshots with `TOUCAN_TEST_SCREENSHOTS` and compare light and dark screens.
   Check menus and flyouts in the running app; headless captures omit popups.
