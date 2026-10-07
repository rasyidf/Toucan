---
title: Editor syntax highlighting
status: planned
progress: "0/8 tasks"
updated: 2026-10-07
summary: Plan translation-aware highlighting in the focused editor and syntax-colored source previews. Implementation has not started; compatibility and performance must be verified first.
---

# Editor syntax highlighting

## Goal

Make placeholders, ICU messages, and markup easier to read while translating. Show source snippets in their original language syntax. Start editable highlighting in the one-key focused editor; keep list editing lightweight until performance is measured.

This is a future implementation plan. The current editor uses ordinary text boxes, and no highlighting dependency has been added.

## Design

Use theme-aware token colors with readable contrast in light and dark themes. Distinguish placeholders, markup, and ICU structural syntax without changing the stored text. Keep validation diagnostics separate from syntax colors so users can distinguish an error from an ordinary token.

Evaluate [AvaloniaEdit and its TextMate integration](https://github.com/AvaloniaUI/AvaloniaEdit) for source previews and focused editing. The app currently uses Avalonia 12.0.5 and FluentAvaloniaUI 3.0.0; compatibility is an implementation gate, not an established fact. TextMate offers grammars and themes, but translation token parsing needs its own design.

Source previews select a grammar from the file extension, with plain text as a fallback. Translation values use translation-aware tokenization rather than treating arbitrary prose as source code. Support brace placeholders, printf placeholders, numbered templates such as `%1`, markup, and ICU arguments and plural/select branches. Nested ICU structures need a parser; a single regular expression is insufficient.

Inspect `PlaceholderMismatchRule` and existing format handling before extracting shared tokenization. Reuse semantics where they match, and preserve existing validation behavior. Do not make highlighting a prerequisite for validation or saving.

## Implementation sequence

- [ ] Build a compatibility spike for AvaloniaEdit/TextMate with the installed Avalonia and FluentAvalonia versions. Confirm rendering, binding, theme changes, and disposal. Record the supported package versions before adding production dependencies.
- [ ] Add read-only syntax-colored source snippets using extension-based grammar selection and a plain-text fallback. Preserve line numbers and navigation to the original file.
- [ ] Define token categories and translation-aware parsing for placeholders, markup, and ICU structures. Add targeted cases for escaped delimiters, malformed input, and nested plurals/selects; preserve text exactly.
- [ ] Integrate editable highlighting into the one-key focused editor. Preserve value binding, multiline input, undo/redo, selection, copy/paste, suggestions, and existing commands.
- [ ] Preserve mode behavior: Editor and Review allow existing editing actions; Audit remains read-only. Changing keys or filters must not leak undo history or caret state between values.
- [ ] Verify keyboard navigation, IME composition, RTL and mixed-direction text, caret positioning, accessibility, theme contrast, and live theme changes.
- [ ] Measure typing latency, large values, repeated key switching, allocations, and resource cleanup. Keep ordinary list rows unless measured results justify broader integration; avoid constructing a heavyweight editor for every row.
- [ ] Add regression coverage and rendered previews for supported token types, malformed input, source fallback, mode permissions, and light/dark themes. Document the final supported syntax and any limitations.

## Acceptance criteria

Typing and saving round-trip the exact value. Highlighting must not alter escaping, whitespace, encoding, or line endings. Placeholder and ICU colors update during edits without moving the caret or breaking composition. Unknown source languages and malformed messages remain readable and editable. Existing translation suggestions and validation continue to work.

List performance and focused navigation must remain responsive on large projects. A highlighting failure falls back to ordinary text editing rather than preventing translation work.

## Scope boundaries

This plan covers visual syntax highlighting and the editor integration needed for it. It does not add a full source-code IDE, language server, formatter, or automatic message rewriting. List-wide editable highlighting is conditional on performance evidence.
