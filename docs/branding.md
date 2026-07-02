# Toucan — Brand Guidelines

> Teach your app every language.

---

## Brand Overview

**Toucan** is a professional translation resource editor for developers and localization teams.

It combines the speed of a modern IDE with the completeness of a localization platform — offline-first, format-agnostic, and AI-assisted.

---

## Mission

Help every application speak every language.

## Vision

The VS Code of localization — a tool developers actually enjoy using.

---

## Positioning

| Level | Statement |
|-------|-----------|
| Primary | The Localization IDE |
| Secondary | Professional translation resource editor |
| Elevator | Toucan handles 14 i18n formats with AI translation, validation, and source code scanning — all offline, all in one workspace. |

---

## Taglines

Primary: **Teach your app every language.**

Alternatives:
- Code once. Speak everywhere.
- Localization without spreadsheets.
- Two languages? Toucan.
- Ship globally.

---

## Brand Pillars

| Pillar | Meaning |
|--------|---------|
| Developer First | Built for engineers — IDE-familiar patterns, keyboard-driven |
| Structured | Translations are data. Treat them like source code. |
| Fast | 10K keys feel instant. Zero-allocation search. |
| Confident | Validation, TM, auditing — eliminate mistakes before deploy |
| Friendly | Powerful but never intimidating |

---

## Personality

Professional · Modern · Curious · Helpful · Playful · Reliable

Never childish. Never enterprise-corporate.

---

## Voice

Speak like an experienced engineer. Clear. Short. Confident.

Prefer "Missing translation" over "Localization asset unavailable."
Prefer "Ready" over "Pipeline completed successfully."

---

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

---

## Logo

### Philosophy
Recognizable at a glance. Modern, friendly, premium, geometric.

### Construction
Built on golden-ratio circles. No arbitrary curves. Works from 16px to 1024px.

### Symbolism
- **Head** — knowledge
- **Beak** — communication
- **Eye** — understanding
- **Colors** — many languages working together

### Requirements
- Readable as a silhouette
- No tiny details that vanish at small sizes
- Never stretched, always proportional

---

## Product Identity

### Toucan IS
- Translation resource editor
- Localization IDE
- Validation platform
- Developer tool
- Language workspace

### Toucan IS NOT
- Translation agency software
- Cloud-only SaaS
- Spreadsheet editor
- CAT tool (no TM segment-level alignment)

---

## UI Identity (v1.0)

### Layout
Three-pane VS Code-style:
- **Left** — tree/list sidebar (200px default)
- **Center** — translation editor (paginated or infinite scroll)
- **Right** — inspector panel (stats, suggestions, details, validation)

### Chrome
- **Title bar** — logo + segmented menu (File, Edit, Tools, Find, View, Help)
- **Toolbar** — Snipping Tool-style pill-grouped icon buttons, mode selector on right
- **Footer bar** — Photos-style action bar (left: quick actions, center: status, right: info panels)
- **No toolbar on start screen** — clean landing with just backdrop

### Modes
Three editor modes with visual differentiation:
- **Editor** (default) — full editing, MT suggestions
- **Review** — approve/reject focus, validation warnings
- **Audit** — read-only, change history

### Backdrop
Mica (Windows 11) with system theme. Start screen shows through backdrop (no opaque background).

### Zen Mode
All chrome hidden. Single translation card centered. J/K navigation. Mode-aware.

---

## File Identity

| Item | Value |
|------|-------|
| Project manifest | `toucan.tproj` |
| File extension | `.tproj` |
| MIME type | `application/json` |
| Registry ProgId | `Toucan.Project` |
| Config folder | `~/.toucan/` or `%DOCUMENTS%/Toucan/` |
| Provider secrets | DPAPI-encrypted `providers.json` |

---

## Typography

- UI: System font (Segoe UI Variable on Windows 11)
- Code/keys: Cascadia Code / system monospace
- Sizes: follow WinUI Fluent type ramp

---

## Motion

- Hover: subtle background fill (SubtleFillColorSecondaryBrush)
- Pressed: slightly darker fill (SubtleFillColorTertiaryBrush)
- Transitions: 150ms ease-out for panel show/hide
- Never bouncing, never exaggerated

---

## Iconography

- Fluent System Icons (via WPF UI SymbolIcon)
- 16px for inline, 20px for toolbar, 24px for status bar
- Consistent weight across the app

---

## Experience Principles

Fast · Predictable · Dense · Keyboard-driven · Offline-first · Search-first · Developer-friendly

---

## Future Product Extensions

| Name | Purpose |
|------|---------|
| Toucan CLI | `toucan check`, `toucan translate`, `toucan export` |
| Toucan AI | ConsistencyAI, quality scoring, tone enforcement |
| Toucan Hub | Collaboration server (locking, presence) |
| Toucan SDK | Plugin system for custom formats/providers/rules |

---

## Copyright

© 2023–2026 Muhammad Fahmi Rasyid (rasyid.dev). MIT License.
