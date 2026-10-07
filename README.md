<div align="center">
  <img width="64" height="64" src="docs/assets/logo.png" alt="Toucan icon"/>
  <h1>Toucan</h1>
</div>

Toucan is a desktop editor for translation files on Windows, macOS, and Linux. It opens 14 formats in one workspace, so you can translate, review, and validate every language file in a project without switching tools.

Toucan is in preview: settings and project files can change between releases until 1.0. Current release: **v0.19.0** on Windows, macOS and Linux (v0.18.0 was the first release on macOS and Linux). The old WPF app was retired after v0.17.3 and lives on the [`legacy/wpf`](https://github.com/rasyidf/Toucan/tree/legacy/wpf) branch. Website: [toucan.rasyid.dev](https://toucan.rasyid.dev).

<img width="878" height="668" alt="Toucan editor with the tree sidebar, translation grid, and inspector panel" src="https://github.com/user-attachments/assets/6c60208e-640a-4fbc-b280-63f5fc856ece" />

<img width="1314" height="848" alt="image" src="https://github.com/user-attachments/assets/e4e27e02-c0b5-4bf8-bfac-9ac59a3c69e5" />


## Quick start

1. Download the build for your platform from [GitHub Releases](https://github.com/rasyidf/Toucan/releases): v0.19.0 for Windows (portable x64 zip; no installer yet), macOS (`Toucan.app` in a DMG or zip) and Linux (tarball).
   macOS: Toucan is free and not signed with a paid Apple Developer ID, so Gatekeeper says it "could not verify" the app on first launch. Open it once, then go to System Settings > Privacy & Security and click **Open Anyway** (on macOS 14 and earlier, right-click `Toucan.app` > Open works too). Or clear the download flag: `xattr -dr com.apple.quarantine Toucan.app`.
2. Open a folder that contains translation files, or create a new project.
3. Translate, review, and save.

Toucan detects the framework when you drop a folder (i18next, Android, Flutter, .NET, iOS, Rails, Gettext, and others) and groups files to match.

## Platforms

| App | Platforms | Status |
|-----|-----------|--------|
| Toucan (Avalonia) | Windows, macOS, Linux | Preview, v0.19.0. The supported app on every platform: Editor/Review/Audit modes, Zen mode, command palette, search and bulk edits, side panels, plugins, file association and Indonesian localization. The Windows builds are this app. |
| `toucan` CLI | Any OS with .NET 10 | Preview. `check`, `stats`, `translate`, `export`, `list-formats`, `list-keys`, `get`, `set`. |

## Supported formats

JSON, YAML, PO, RESX, Android XML, iOS `.strings`, XLIFF, ARB, CSV, TOML, INI, Java `.properties`, Laravel PHP, and Excel. Projects from `.babel` files can be imported. Some formats lose detail on save (PO plurals, ARB `@key` metadata, XLIFF source text); see [docs/known-bugs.md](docs/known-bugs.md).

## Features

**Editing.** The editor has three modes. Editor mode shows inline suggestions from translation memory: focus an empty field and the best match appears as faint ghost text; press Tab to accept it. Review mode filters to unapproved items and lets you approve or reject each one, with validation warnings shown alongside. Audit mode is read-only and shows approval state and change history.

The layout follows VS Code: a tree or list sidebar, a translation pane (paginated or infinite scroll), and an inspector for stats, suggestions, key details, and validation results. Zen mode hides everything except the editor. Press Cmd/Ctrl+Shift+P for the command palette, which finds any menu command and shows its shortcut, and Cmd/Ctrl+/ opens a sheet of every keyboard shortcut.

**Machine translation.** Pre-translate with Google Translate, DeepL, Microsoft Translator, OpenAI, Claude, or Gemini. Results appear in a preview and are only written when you commit them. Placeholders (`{{var}}`, `{0}`, `%s`, `:param`) are preserved, formality settings are respected, and you can target a single key, a namespace, or a language.

**Translation memory.** Fuzzy matching runs on a trigram engine and reuses translations across projects, with a configurable threshold and scope. Matches show in the inspector as you type. TMX import and export are supported.

**Validation.** Six rules run on save and on demand: missing translations, placeholder mismatches, duplicate keys, untranslated copies, empty values, and whitespace mismatches. Each rule can be turned off or given a severity, and projects can add their own rules (max length, forbidden words, regex).

**Search and bulk edits.** Search and replace across keys, values, and languages with regex and a preview. Select many keys to delete, move, pre-translate, approve, or copy source to target in one step.

**Source scanning.** Toucan scans your code for `t('key')` calls in `.tsx`, `.vue`, `.svelte`, `.py`, and other files. You can filter keys by used or unused, and double-clicking a key opens the source file in your editor.

**Plurals and arrays.** Supports i18next `_one`/`_other` suffixes, ICU plural forms, JSON array values, and gender form generation.

**Packages.** A project can hold several translation sets, such as `ui.json`, `errors.json`, and `emails.json`.

<img width="904" height="696" alt="Toucan review mode with validation warnings" src="https://github.com/user-attachments/assets/bbd9b2ba-99e6-4500-a4e4-d92d602f33ff" />

## Configuration

- Provider API keys can be set app-wide or per project. They are encrypted with DPAPI on Windows, and with AES-GCM and a per-user key file on macOS and Linux. See [docs/provider-settings.md](docs/provider-settings.md).
- Pre-translation runs as a dry run first and needs an explicit commit. See [docs/pretranslation-preview.md](docs/pretranslation-preview.md).
- The default language is set per user under Settings > Options (default: en-US).

## Roadmap

The planned order to 1.0:

| Version | Planned |
|---------|---------|
| v0.18 | First macOS and Linux release, plugins (preview). |
| v0.19 | Command palette, a new title bar, Claude and Gemini providers, a UI polish pass, and the Avalonia app on Windows, which replaces the deprecated WPF app. Shipped. |
| v0.20 | Onboarding and UX polish: first-run wizard, tooltip tour, empty-state hints. Ghost-text suggestions and the keyboard shortcut sheet are in (unreleased). |
| v0.21 | ConsistencyAI, which batch-checks translations for tone, placeholders, and accuracy. Project glossary. |
| v0.22 | Signed packages, a release pipeline, and CI. |
| v0.23 | Review lifecycle (Draft, Review, Approved, Published). Git integration with per-key diffs. |
| v0.24 | Auto-updater with stable and preview channels. Editor improvements. |
| v1.0 | Performance at scale and stabilization. The first stable release. |
| After 1.0 | Imports from Crowdin, Lokalise, Phrase, and Transifex. Toucan Hub for locking and presence. A GitHub Action for `toucan check`. Avalonia feature parity. |

This is the planned order, not a promise. Details are in [docs/todos/future-roadmap.md](docs/todos/future-roadmap.md); shipped features are listed in [docs/completed-features.md](docs/completed-features.md). Every doc's status and summary is in [docs/INDEX.md](docs/INDEX.md).

## Tech stack

| Layer | Technology |
|-------|-----------|
| UI | [Avalonia](https://avaloniaui.net) 12 with FluentAvaloniaUI (Windows, macOS, Linux) |
| Architecture | MVVM with CommunityToolkit.Mvvm |
| Runtime | .NET 10, System.Text.Json |
| Providers | Google, DeepL, Microsoft, OpenAI, Claude, Gemini, custom webhook |
| Format engine | Strategy pattern (`ILoadStrategy` / `ISaveStrategy`), string format IDs |
| Extensibility | Plugins loaded from `.dll` assemblies ([guide](docs/plugins.md)), contract package `Toucan.Plugins.Abstractions` |

## Plugins

Plugins add file formats, translation providers, validation rules, and framework profiles from `.dll` assemblies, without changing Toucan. They arrived in v0.18.0, work in the Avalonia app and the CLI, and load only when you have enabled and trusted them: Settings > Plugins in the app, or `toucan plugins list` and `toucan plugins trust <id>` on the command line. To write one, start from [`samples/Toucan.Sample.Plugin`](samples/Toucan.Sample.Plugin) and the [plugin guide](docs/plugins.md); the contracts ship as the `Toucan.Plugins.Abstractions` package.

## Build from source

The cross-platform projects (core library, CLI, Avalonia app, plugin contracts, tests) build and test on macOS, Linux, and Windows:

```bash
dotnet test Toucan.CrossPlatform.slnx
dotnet run --project Toucan.Avalonia
dotnet run --project Toucan.CLI -- check ./locales
```

The old WPF app is on the `legacy/wpf` branch. Architecture notes are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

The app version is set once, in `Directory.Build.props`. `publish.ps1` (Windows packages), `packaging/Build-Msix.ps1`, and `packaging/build-macos-app.sh` read it from there.

## Contributing

Report bugs and request features in [GitHub Issues](https://github.com/rasyidf/Toucan/issues). Pull requests are welcome.

## License

[MIT](LICENSE.txt), copyright 2023–2026 Muhammad Fahmi Rasyid ([rasyid.dev](https://rasyid.dev)).
