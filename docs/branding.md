---
title: "Toucan: Brand Guidelines"
status: reference
updated: 2026-10-07
summary: "Brand guidelines: name, voice, colors, logo use. Platform line updated for v0.20.1 (Avalonia on all platforms)."
---
# Toucan: Brand Guidelines

> Every language, side by side.

## Overview

Toucan is a desktop editor for translation files, made for developers and localization teams. It reads 14 formats, works offline, and adds validation, translation memory and opt-in AI translation.

Release status: preview until 1.0. Current release: v0.20.1 (Windows, macOS, Linux). The WPF app is deprecated (last release v0.17.3). Say "preview" wherever a version or download is offered.

Platforms: Windows, macOS and Linux (Avalonia app), and the `toucan` CLI.

## Mission

Make shipping an app in many languages as routine as shipping it in one.

## Vision

A localization editor that works like the code editor developers already use.

## Positioning

| Level | Statement |
|-------|-----------|
| Primary | Desktop editor for translation files |
| Secondary | Translation resource editor |
| Elevator | Toucan reads 14 translation formats, validates them, translates with machine providers or opt-in AI, and scans source code for unused keys. Files stay on your machine. |

## Taglines

Primary: Every language, side by side.

Alternatives:
- Localization without spreadsheets.
- Check your translations before you ship.

## Brand Pillars

| Pillar | Meaning |
|--------|---------|
| Developer first | IDE-style layout and keyboard shortcuts |
| Structured | Translations are data, so they get validation rules the way code gets lint rules |
| Fast | 10K keys feel instant. Zero-allocation search. |
| Confident | Validation, translation memory and audit mode catch mistakes before deploy |
| Friendly | Capable without being intimidating |

## Personality

Professional, curious, playful, reliable. Never childish, never corporate.

## Voice

Write like an experienced engineer: short, clear, direct.

Prefer "Missing translation" over "Localization asset unavailable."
Prefer "Ready" over "Pipeline completed successfully."

## Color Palette

| Name | Hex | Usage |
|------|-----|-------|
| Primary Blue | `#2196F3` | Logo text, primary actions, accent |
| Deep Blue | `#1764E8` | Hover states, selected items |
| Sky | `#3CC9FF` | Secondary highlights |
| Golden Yellow | `#FFC93C` | Warnings, caution states |
| Warm Orange | `#FF9A3C` | Errors, attention |
| Purple Accent | `#6F5BFF` | Special badges, AI features |
| Background | `#171B22` | Dark theme base |
| Surface | `#232936` | Cards, panels |
| Border | `#3A4153` | Subtle separators |
| Text | `#FFFFFF` | Primary text (dark theme) |
| Muted | `#A5ACBA` | Secondary text, hints |

Light theme follows system WinUI Fluent tokens (Mica backdrop).

## Logo

### Philosophy
Recognizable at a glance, geometric, friendly.

### Construction
Built on golden-ratio circles, with no arbitrary curves. Works from 16px to 1024px.

### Symbolism
- Head: knowledge
- Beak: communication
- Eye: understanding
- Colors: many languages working together

### Requirements
- Readable as a silhouette
- No small details that disappear at small sizes
- Never stretched, always proportional

## Product Identity

### Toucan is
- An editor for translation resource files
- A validator for placeholders and consistency
- A developer tool

### Toucan is not
- Translation agency software
- Cloud-only SaaS
- A spreadsheet editor
- A CAT tool (no TM segment-level alignment)

## UI Identity (Windows app, v0.17)

### Layout
Three panes, laid out like VS Code:
- Left: tree/list sidebar (200px default)
- Center: translation editor (paginated or infinite scroll)
- Right: inspector panel (stats, suggestions, details, validation)

### Chrome
- Title bar: logo and segmented menu (File, Edit, Tools, Find, View, Help)
- Toolbar: Snipping Tool-style pill-grouped icon buttons, mode selector on the right
- Footer bar: Photos-style action bar (left: quick actions, center: status, right: info panels)
- Start screen: no toolbar, only the backdrop

### Modes
Three editor modes, each visually distinct:
- Editor (default): full editing, MT suggestions
- Review: approve/reject, validation warnings
- Audit: read-only, change history

### Backdrop
Mica (Windows 11) with the system theme. The start screen shows through the backdrop with no opaque background.

### Zen Mode
All chrome hidden. One translation card, centered. J/K navigation. Follows the current mode.

## File Identity

| Item | Value |
|------|-------|
| Project manifest | `toucan.tproj` |
| File extension | `.tproj` |
| MIME type | `application/json` |
| Registry ProgId | `Toucan.Project` |
| App settings folder | `Documents/Toucan/` (providers, layout, plugins) |
| Project settings folder | `<project>/.toucan/` |
| Provider secrets | `providers.json`; secrets encrypted with DPAPI on Windows, AES-GCM on macOS and Linux |

## Typography

- UI: system font (Segoe UI Variable on Windows 11)
- Code and keys: Cascadia Code, or the system monospace font
- Sizes: follow the WinUI Fluent type ramp
- Website (`docs/index.html`): headings in Bricolage Grotesque, body in the system UI font, code in Cascadia Code with JetBrains Mono as the web fallback

## Motion

- Hover: light background fill (SubtleFillColorSecondaryBrush)
- Pressed: slightly darker fill (SubtleFillColorTertiaryBrush)
- Panel show/hide: 150ms ease-out
- No bouncing, no exaggerated easing

## Iconography

- Fluent System Icons
- 16px inline, 20px in the toolbar, 24px in the status bar
- Same stroke weight throughout the app

## Experience Principles

Fast, predictable, dense, keyboard-driven, offline-first, search-first, developer-friendly.

## Product Extensions

| Name | Purpose | Status |
|------|---------|--------|
| Toucan CLI | `toucan check`, `toucan translate`, `toucan export` | Preview, shipped since v0.15 |
| Toucan SDK | Plugin system for custom formats/providers/rules | Preview since v0.18.0 (`Toucan.Plugins.Abstractions`) |
| Toucan AI | ConsistencyAI, quality scoring, tone enforcement | Planned (ConsistencyAI in v0.21) |
| Toucan Hub | Collaboration server (locking, presence) | After 1.0 |

## Copyright

© 2023–2026 Muhammad Fahmi Rasyid (rasyid.dev). MIT License.
