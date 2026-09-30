<div align="center">
  <img width="64" height="64" src="https://user-images.githubusercontent.com/28984914/216422726-a1597ef2-836b-4c31-8229-0b267c2b7e52.png" alt="Toucan icon"/>
  <h1>Toucan</h1>
</div>

Toucan is a Windows desktop editor for translation files. It opens 14 formats in one workspace, so you can translate, review, and validate every language file in a project without switching tools.

<img width="878" height="668" alt="Toucan editor with the tree sidebar, translation grid, and inspector panel" src="https://github.com/user-attachments/assets/6c60208e-640a-4fbc-b280-63f5fc856ece" />

## Quick start

1. Download the latest installer from [GitHub Releases](https://github.com/rasyidf/Toucan/releases) and run it.
2. Open a folder that contains translation files, or create a new project.
3. Translate, review, and save.

Toucan detects the framework when you drop a folder (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, and others) and groups files to match.

## Supported formats

JSON, YAML, PO, RESX, Android XML, iOS `.strings`, XLIFF, ARB, CSV, TOML, INI, Java `.properties`, Laravel PHP, and Excel. Projects from `.babel` files can be imported.

## Features

**Editing.** The editor has three modes. Editor mode shows inline suggestions from translation memory. Review mode filters to unapproved items and lets you approve or reject each one, with validation warnings shown alongside. Audit mode is read-only and shows approval state and change history.

The layout follows VS Code: a tree or list sidebar, a translation pane (paginated or infinite scroll), and an inspector for stats, suggestions, key details, and validation results. Zen mode hides everything except the editor.

**Machine translation.** Pre-translate with Google Translate, DeepL, Microsoft Translator, or OpenAI. Results appear in a preview and are only written when you commit them. Placeholders (`{{var}}`, `{0}`, `%s`, `:param`) are preserved, formality settings are respected, and you can target a single key, a namespace, or a language.

**Translation memory.** Fuzzy matching runs on a trigram engine and reuses translations across projects. Matches are suggested as you type.

**Validation.** Six rules run on save and on demand: missing translations, placeholder mismatches, duplicate keys, untranslated copies, empty values, and whitespace mismatches.

**Source scanning.** Toucan scans your code for `t('key')` calls in `.tsx`, `.vue`, `.svelte`, `.py`, and other files. You can filter keys by used or unused, and double-clicking a key opens the source file in your editor.

**Plurals and arrays.** Supports i18next `_one`/`_other` suffixes, ICU plural forms, JSON array values, and gender form generation.

**Packages.** A project can hold several translation sets, such as `ui.json`, `errors.json`, and `emails.json`.

<img width="904" height="696" alt="Toucan review mode with validation warnings" src="https://github.com/user-attachments/assets/bbd9b2ba-99e6-4500-a4e4-d92d602f33ff" />

## Configuration

- Provider API keys can be set app-wide or per project and are encrypted with DPAPI. See [docs/provider-settings.md](docs/provider-settings.md).
- Pre-translation runs as a dry run first and needs an explicit commit. See [docs/pretranslation-preview.md](docs/pretranslation-preview.md).
- The default language is set per user under Settings > Options (default: en-US).

## Roadmap

Version 1.0 is current and focuses on test coverage, performance profiling, and MSIX packaging.

| Version | Planned |
|---------|---------|
| 1.1 | Auto-updater with stable and preview channels. ConsistencyAI, which batch-checks translations for tone, placeholders, and accuracy. Embedding-based translation memory with inline ghost text and TMX import/export. A CLI with `toucan check`, `translate`, `export`, and `stats`. |
| 1.2 | Review workflow (Draft, Review, Approved, Published). Git integration with branch awareness, per-key diffs, and auto-commit. Webhook notifications. |
| 1.3 | Avalonia port for macOS and Linux, sharing the ViewModel layer. |
| 2.0 | Plugin system for custom formats, providers, and validation rules, loaded as `.dll` assemblies. Imports from Crowdin, Lokalise, Phrase, and Transifex. |

Details are in [docs/todos/future-roadmap.md](docs/todos/future-roadmap.md).

## Tech stack

| Layer | Technology |
|-------|-----------|
| UI | WPF with [WPF UI](https://github.com/lepoco/wpfui) (Fluent Design, Mica backdrop) |
| Architecture | MVVM with CommunityToolkit.Mvvm |
| Runtime | .NET 10, System.Text.Json |
| Providers | Google, DeepL, Microsoft, OpenAI, custom webhook |
| Format engine | Strategy pattern (`ILoadStrategy` / `ISaveStrategy`) |

## Contributing

Report bugs and request features in [GitHub Issues](https://github.com/rasyidf/Toucan/issues). Pull requests are welcome.

## License

[MIT](LICENSE.txt), copyright Rasyidf 2023-2026.
