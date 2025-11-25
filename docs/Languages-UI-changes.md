# Languages View — UI Changes & Compact Mode

This note describes the UI changes planned for the `LanguagesView` and related ViewModel commands.

## Goals

- Add a clear Show/Hide toggle for the whole Languages panel.
- Improve the collapsed mode of language entries to be more informative and compact (show percent, counts, and a quick action icon).
- Remove the bottom-wide Advanced Options panel and replace those actions with per-language context menus (right-click) and per-language action buttons.

## User scenarios

- Show / Hide:
  - Users can collapse or expand the entire Languages panel quickly using a header toggle.
  - Preference persisted between sessions (future work).

- Compact / Collapsed language entry (informative):
  - When a language entry is collapsed, show: language name, percentage complete, translations done/total (compact), and quick action menu button (three dots) for common actions.

- Per-language context menu actions:
  - Pre-translate > For single key, For namespace, For whole language
  - Statistics (still supported per-language)
  - Hide/remove language

## Details & small notes

- Remove the advanced options panel (at bottom of LanguagesView) and wire existing commands (PreTranslateBulkCommand, GenerateStatisticsBulkCommand) into per-language flows and a new centralized batch mechanism later.

### What changed (implemented)

- The bottom advanced options panel has been removed in favor of per-language context menus and a compact header.
- Each language entry now exposes a context menu with: Pre-translate › Key, Pre-translate › Namespace, Pre-translate › Language, Statistics, Hide.
- The header is more compact and shows the percent complete and translation stats directly in the collapsed view.
- The top-right control now includes a Show/Hide toggle for the entire Languages panel.

### How to use (brief)

1. Right-click on any language item in the Languages panel and pick "Pre-translate › Language" to automatically pre-translate available items for that language.
2. Use "Pre-translate › Namespace" to target a specific namespace (you will be prompted for the namespace prefix or exact key).
3. Use "Pre-translate › Key" to pre-translate a single translation key (you will be prompted for the key id).

- Keep the header App-level expand / collapse buttons (Expand All / Toggle) but ensure the UI is responsive and compact.

## Migration strategy

1. Create UI changes (XAML) and small ViewModel changes so the panel is hidden.
2. Add per-language context menu and commands in ViewModel to call pretranslation service.
3. Ensure existing commands are reworked to call provider layer (when available) — introduce a compatibility path to avoid breaking existing flows.

## UX / next steps

- Add progress UI for pretranslation operations.
- Add small modal confirmations when pre-translating full languages (danger/confirmation flow for large jobs).
