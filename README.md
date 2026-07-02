<div align="center">
  <img width="64" height="64" src="https://user-images.githubusercontent.com/28984914/216422726-a1597ef2-836b-4c31-8229-0b267c2b7e52.png" alt="Toucan Icon" style="margin-bottom: 8px"/>
  <h1 style="font-size: 36px; line-height: 1.2; font-weight: 700; margin-top: 0; margin-bottom: 8px;">Toucan</h1>
  <p>A professional <strong>translation resource editor</strong> for developers and localization teams.</p>
  <p>14 formats · 4 AI providers · translation memory · source code scanning · offline-first.</p>
</div>

---

## What is Toucan?

Toucan is a desktop i18n editor that handles every format your project uses — JSON, YAML, PO, RESX, Android XML, iOS .strings, XLIFF, ARB, CSV, TOML, INI, Java .properties, Laravel PHP, and Excel. It provides a unified workspace where you can translate, review, and validate all your language files without switching tools.

Built for Windows with WPF and Fluent Design (Mica backdrop, system theme).

---

## Key Features

### Multi-Format Support
Load and save 14 translation formats. Auto-detect framework on folder drop (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, and more). Import from `.babel` projects.

### Editor Modes
- **Editor** — translate with inline suggestions and translation memory
- **Review** — approve/reject with validation warnings, auto-filters unapproved items
- **Audit** — read-only view with approval state and change history

### Machine Translation
Pre-translate using Google Translate, DeepL, Microsoft Translator, or OpenAI. Preview before applying. Preserves placeholders (`{{var}}`, `{0}`, `%s`, `:param`), respects formality settings, and supports per-key/namespace/language targeting.

### Translation Memory
Fuzzy matching with zero-allocation trigram engine. Reuses translations across projects. Suggests matches as you type.

### Validation Pipeline
6 built-in rules: missing translations, placeholder mismatches, duplicate keys, untranslated copies, empty values, whitespace mismatches. Runs on save and on-demand.

### Source Code Integration
Scans your codebase for `t('key')` patterns across `.tsx`, `.vue`, `.svelte`, `.py` and more. Filter used/unused keys. Double-click opens the source file in your editor.

### Three-Pane Layout
VS Code-style workspace: tree/list sidebar · translation editor (paginated or infinite scroll) · inspector panel (stats, suggestions, details, validation). Zen mode for distraction-free editing.

### Plural & Array Support
i18next `_one/_other` suffixes and ICU plural forms. Array values in JSON. Gender form generation.

### Packages
Manage multiple translation sets per project (e.g., `ui.json`, `errors.json`, `emails.json`).

---

## Quick Start

1. Download the latest release from **[GitHub Releases](https://github.com/rasyidf/Toucan/releases)**
2. Run the installer
3. Open a folder containing your translation files, or create a new project
4. Translate, review, save

Toucan auto-detects your framework and organizes files accordingly.

---

## Configuration

- **Provider settings:** App-level or project-level API keys, encrypted with DPAPI. See [docs/provider-settings.md](docs/provider-settings.md).
- **Pre-translation preview:** Dry-run mode with explicit commit step. See [docs/pretranslation-preview.md](docs/pretranslation-preview.md).
- **Default language:** Set per-user in Settings → Options (default: en-US).

---

## Roadmap

### v1.0 (current) — Stability & Polish
All 88 planned features complete. Focus on test coverage, performance profiling, and MSIX packaging.

### v1.1 — Quality of Life
- Auto-updater (stable/preview channels)
- ConsistencyAI (batch-check translations for tone, placeholders, accuracy)
- Enhanced translation memory (embedding-based similarity, inline ghost text, TMX import/export)
- CLI tool (`toucan check`, `toucan translate`, `toucan export`, `toucan stats`)

### v1.2 — Collaboration
- Review workflow (Draft → Review → Approved → Published)
- Git integration (branch awareness, diff per key, auto-commit)
- Webhook notifications

### v1.3 — Cross-Platform
- Avalonia port (macOS + Linux)
- Shared ViewModel layer

### v2.0 — Extensibility
- Plugin system (.dll assemblies, custom formats, custom providers, custom validation rules)
- Platform imports (Crowdin, Lokalise, Phrase, Transifex)

See [docs/todos/future-roadmap.md](docs/todos/future-roadmap.md) for full details.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WPF + [WPF UI](https://github.com/lepoco/wpfui) (Fluent Design) |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| Core | .NET 10, System.Text.Json |
| Providers | Google, DeepL, Microsoft, OpenAI, Custom Webhook |
| Format Engine | Strategy pattern (ILoadStrategy / ISaveStrategy) |

---

## Contributing

- Report bugs or request features on **[GitHub Issues](https://github.com/rasyidf/Toucan/issues)**
- Pull requests welcome at **[github.com/rasyidf/Toucan](https://github.com/rasyidf/Toucan)**

---

## License

[MIT](LICENSE.txt) © Rasyidf 2023–2026
