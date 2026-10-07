---
title: "Known Issues & Unfinished Features"
status: active
updated: 2026-10-07
summary: "Open issues for v0.19.0, each with repro, cause and fix direction: 9 format bugs (PO, CSV, RESX, XLIFF, ARB, YAML), release gaps, missing panels. Fixed bugs live in CHANGELOG.md."
---
# Known Issues & Unfinished Features

Current release: **v0.19.0** (preview). Last audited **2026-10-07** against the code on `fix/ui-visual-polish`.

This file lists what is wrong *now*. Fixed bugs are not kept here: they are in [CHANGELOG.md](../CHANGELOG.md) (see [Where the old fixes went](#where-the-old-fixes-went)). When a bug is fixed, delete its row and section here and add a `### Fixed` line to the changelog `[Unreleased]` section. Planned work for an ID is linked from [the roadmap](todos/future-roadmap.md) (REL, APP, QA); the roadmap does not describe it again. The WPF app is gone from `main` (source: branch `legacy/wpf`), so nothing here is about WPF.

**How each issue was checked.** "Reproduced" means I ran the case through the real load and save strategies and the output is quoted. "From code" means the cause is visible in the source but I did not run it. Run the same cases again before fixing, and turn each repro into a test in `tests/Toucan.Core.Tests/Formats/`.

## Open issues

| ID | Severity | Area | Problem | Checked |
|----|----------|------|---------|---------|
| [FMT-01](#fmt-01) | Medium | PO | Language is now read correctly, but save still writes `{language}.po` at the project root instead of back to the original `<lang>/LC_MESSAGES/` file | From code |
| [FMT-02](#fmt-02) | High | PO | Plural entries (`msgid_plural`, `msgstr[N]`) are dropped completely, not just their plural forms | Reproduced |
| [FMT-03](#fmt-03) | High | PO | Save rewrites every entry with `msgctxt = msgid = key`, loses the real `msgid` when a context exists, and drops comments, flags and headers | Reproduced |
| [FMT-05](#fmt-05) | Medium | RESX | Any `A.B.resx` file takes `B` as its language (`Views.Home.Index.resx` becomes language `Index`) | Reproduced |
| [FMT-06](#fmt-06) | High | XLIFF | Save writes the key as `<source>` and the first language as `source-language`; real source text, notes and state are lost | Reproduced |
| [FMT-07](#fmt-07) | Medium | ARB | `@key` metadata (description, placeholders) is discarded on save | Reproduced |
| [FMT-08](#fmt-08) | Medium | ARB | Without `@@locale`, `app_en_US.arb` loads as language `US` | Reproduced |
| [FMT-09](#fmt-09) | Low | YAML | Flat dotted keys are rewritten as nested maps; a key that is also a parent gets a `__self` entry | Reproduced |
| [REL-01](#rel-01) | Medium | Release | No CI or release pipeline; every release is built by hand | Reproduced (no config in repo) |
| [REL-02](#rel-02) | Medium | Release | macOS app is ad-hoc signed and not notarized | From docs and script |
| [REL-03](#rel-03) | Medium | Release | No update check anywhere in the app | Reproduced (no code) |
| [REL-04](#rel-04) | Low | Release | Windows ships as a portable zip; no installer or MSIX in releases | From docs |
| [APP-01](#app-01) | Low | App | Source Control and Dictionary panels do not exist | Reproduced |
| [APP-02](#app-02) | Low | App | Keyboard shortcuts cannot be changed | Reproduced |
| [APP-03](#app-03) | Low | App | No notification history; the status-bar badge only shows a count of empty translations | Reproduced |
| [QA-01](#qa-01) | Medium | Tests | No tests for the bugs fixed in 0.17.1, and none for the formats' edge cases above | From code |
| [QA-02](#qa-02) | Info | Perf | Large-project performance has never been profiled | Not checked |

Severity: **High** loses or corrupts user data; **Medium** gives wrong results or blocks a release goal; **Low** is a gap or a cosmetic problem; **Info** is a known unknown.

Suggested order: FMT-01 (save side), FMT-02, FMT-03 and FMT-06 first (data loss in common projects), then FMT-05, FMT-07, FMT-08, then release work, then the rest.

---

## Format bugs (Toucan.Core)

All of these sit in `Toucan.Core/Services/LoadStrategies/` and `.../SaveStrategies/`. Round-trip means: open a folder, change nothing or one value, save.

<a id="fmt-01"></a>
### FMT-01 — PO: language is the file name, so gettext folder layouts collapse

- **Severity:** High · **Area:** PO · **Checked:** reproduced · **Code:** `PoLoadStrategy.cs:21`
- **What happens:** `var lang = Path.GetFileNameWithoutExtension(file);` The standard gettext layout is `<lang>/LC_MESSAGES/<domain>.po`, where the file name is the domain (`messages`) and the folder is the language.
- **Repro:** two files, `fr/LC_MESSAGES/messages.po` and `de/LC_MESSAGES/messages.po`, each with `Language: fr` or `Language: de` in the header. Load the folder.
- **Expected:** languages `fr` and `de`.
- **Actual:** `messages:Hello=X-de | messages:menu=O-de | messages:Hello=X-fr | messages:menu=O-fr`. One language, `messages`, with duplicate keys.
- **Then, on save:** a new `messages.po` appears in the project root with `# Language: messages` and `"Language: messages\n"`. The original `fr/` and `de/` files are left untouched, so nothing is overwritten, but the editor shows one language with duplicated keys and the new file is wrong.
- **Impact:** projects that use the standard gettext folder layout cannot be translated in Toucan. Only projects that name the files by language (`fr.po`, `de.po`) work.
- **Cause:** the loader never reads the `Language:` header or the folder name.
- **Fix direction:** resolve the language in this order: `Language:` header, `.../<lang>/LC_MESSAGES/` folder, file name. Keep the file's original relative path so save writes back to the same file (the save side builds `{language}.po` at the root today).
- **Tests to add:** the repro above (two languages, same key); a flat `fr.po`; header wins over file name.

<a id="fmt-02"></a>
### FMT-02 — PO: plural entries are dropped completely

- **Severity:** High · **Area:** PO · **Checked:** reproduced · **Code:** `PoLoadStrategy.cs:44-62`
- **What happens:** the parser only recognizes `msgid `, `msgstr ` (with a space) and `msgctxt `. `msgid_plural` and `msgstr[0]` / `msgstr[1]` match nothing, so `currentStr` stays null and the entry is discarded at the next blank line.
- **Repro:**
  ```
  msgid "one apple"
  msgid_plural "%d apples"
  msgstr[0] "one apple"
  msgstr[1] "%d apples"

  msgid "hi"
  msgstr "Hello"
  ```
- **Expected:** both entries, the plural one with its forms.
- **Actual:** `en:hi=Hello` only. The plural entry is gone, including `msgstr[0]`.
- **Impact:** the old doc said plural forms are dropped. It is worse: the whole entry disappears from the editor, and the next save removes it from the file. Any gettext project with plurals loses strings silently.
- **Fix direction:** parse `msgid_plural` and `msgstr[N]`. Model a plural entry as one item per form (the app already has plural support for i18next `_one`/`_other` and ICU) or as one item with a form index. Write the `Plural-Forms:` header on save.
- **Tests to add:** load and save a file with 2 forms (en) and 3 forms (ru); a file that mixes plural and normal entries must keep all of them.

<a id="fmt-03"></a>
### FMT-03 — PO: save rewrites every entry and loses `msgid`, comments and headers

- **Severity:** High · **Area:** PO · **Checked:** reproduced · **Code:** `PoSaveStrategy.cs:25-38`, `PoLoadStrategy.cs:67,81`
- **What happens:**
  1. Load uses `key = msgctxt ?? msgid`. When an entry has a context, the real `msgid` is thrown away.
  2. Save writes `msgctxt "<key>"`, `msgid "<key>"`, `msgstr "<value>"` for every entry.
  3. Save writes a fresh header and no comments.
- **Repro:** entries `msgid "Hello"` (no context) and `msgctxt "menu"` + `msgid "Open"`. Load, then save to a flat folder.
- **Actual saved file:**
  ```
  msgctxt "Hello"
  msgid "Hello"
  msgstr "X-de"
  msgctxt "menu"
  msgid "menu"
  msgstr "O-de"
  ```
  `msgid "Open"` became `msgid "menu"`. The `Hello` entry gained a `msgctxt` it never had. The `#: src/a.py:3` reference and the `#, fuzzy` flag are gone.
- **Impact:** gettext looks entries up by `msgctxt` plus `msgid`. A program calling `gettext("Hello")` will not match an entry that now has `msgctxt "Hello"`, so a saved file stops translating at run time. `fuzzy` flags and source references are lost, and the `Plural-Forms` and other headers are replaced.
- **Cause:** the data model has one string per key, so msgid, msgctxt and comments have nowhere to live.
- **Fix direction:** keep `msgctxt`, `msgid`, flags and `#:` references per item. Key as `msgctxt\u0004msgid` (gettext's own separator) or add fields. Write back the original header. Only write `msgctxt` when the entry had one. Honor `#, fuzzy` as "not approved".
- **Tests to add:** byte-for-byte round trip of a small realistic `.po` with context, flags, references and a header; unchanged file must not be modified on save.

<a id="fmt-05"></a>
### FMT-05 — RESX: language detection treats any last name part as a language

- **Severity:** Medium · **Area:** RESX · **Checked:** reproduced · **Code:** `ResxLoadStrategy.cs:42-53`
- **What happens:** `Resources.en-US.resx` gives `en-US`. But any name with two or more dot parts takes the last one if it is 2 to 10 characters long.
- **Repro:** files `Resources.resx`, `Resources.fr.resx`, `Views.Home.Index.resx`, `Resources.Designer.resx`.
- **Actual:** languages `default`, `fr`, `Index`, `Designer`.
- **Impact:** ASP.NET Core resource files (`Views.Home.Index.resx`, `Controllers.HomeController.resx`) show up as fake languages. Their strings are mixed into language lists and statistics, and a later save writes `Resources.Index.resx`.
- **Fix direction:** accept the last part only if it is a valid culture name (`CultureInfo.GetCultureInfo` with predefined cultures only, or a BCP 47 pattern like `^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$`).
- **Tests to add:** the four names above; `Resources.zh-Hans.resx` and `Resources.pt-BR.resx` must still work.

<a id="fmt-06"></a>
### FMT-06 — XLIFF: save writes the key as `<source>`; source text, notes and state are lost

- **Severity:** High · **Area:** XLIFF · **Checked:** reproduced · **Code:** `XliffSaveStrategy.cs:23,34,39`, `XliffLoadStrategy.cs:44-47`
- **What happens:** `<source>` is set to the key (`item.Namespace`), `source-language` is `context.Languages.FirstOrDefault()` (the first language in the list, not the project's primary language), and nothing else from the original file is kept.
- **Repro:** an Angular-style file `messages.fr.xlf` with `id="a1b2c3"`, `<source>Welcome</source>`, `<target state="translated">Bienvenue</target>`, a `<note>` and `datatype="html"`. Load, then save.
- **Actual saved `fr.xlf`:** `source-language="fr" target-language="fr"`, `<source>a1b2c3</source>`, `<target>Bienvenue</target>`. The English `Welcome`, the note, `state` and `datatype` are gone.
- **Where the original is overwritten:** the save target is `{language}.xlf` in the project folder. In this repro the original `messages.fr.xlf` survived and a second file was created. If your file is already named `fr.xlf`, it is overwritten with the degraded version.
- **Impact:** the editor shows hash IDs only, with no source text to translate from. A saved file has no source text, which tools that match on source text (Angular's i18n merge, CAT tools) depend on.
- **Fix direction:** load `<source>` into the item (a "source text" field shown in the editor and in validation) and write it back. Preserve `<note>`, `state`, `datatype` and `original`, or edit the file in place instead of regenerating it. Use the project's primary language for `source-language`. Support XLIFF 2.0 on save, or refuse to save a 2.0 file as 1.2.
- **Tests to add:** the Angular file above must round trip with source, note and state intact.

<a id="fmt-07"></a>
### FMT-07 — ARB: `@key` metadata is discarded on save

- **Severity:** Medium · **Area:** ARB · **Checked:** reproduced · **Code:** `ArbLoadStrategy.cs:38-39`, `ArbSaveStrategy.cs:26-28`
- **Repro:** `{"@@locale":"en","hi":"Hi {n}","@hi":{"description":"greeting","placeholders":{"n":{}}}}`. Load, then save.
- **Actual:** `{ "@@locale": "en", "hi": "Hi {n}" }`. The `@hi` block is gone.
- **Impact:** Flutter's `gen-l10n` reads the `placeholders` block for messages that take arguments, so a project can stop building after one save from Toucan. Descriptions for translators are lost too.
- **Fix direction:** keep each `@key` object per item (opaque JSON is enough) and write it back next to its key. Do not drop other `@@` metadata such as `@@last_modified` either.
- **Tests to add:** the repro; unknown `@@` keys survive; key order is stable.

<a id="fmt-08"></a>
### FMT-08 — ARB: locale from the file name breaks `app_en_US.arb`

- **Severity:** Medium · **Area:** ARB · **Checked:** reproduced · **Code:** `ArbLoadStrategy.cs:31-33`
- **What happens:** without `@@locale`, the name is split on `_` and the last part is the language.
- **Repro:** `app_en_US.arb` containing `{"hi":"Hi"}`.
- **Actual:** language `US`.
- **Impact:** region locales load as the region code, so they collide with other files and save as `app_US.arb`. Most real ARB files include `@@locale`, which is why this is Medium.
- **Fix direction:** if the last part is an uppercase region and the one before it is a language, join them as `en_US` (normalize to `en-US` inside Toucan). Write `app_<locale>.arb` using the original separator style.
- **Tests to add:** `app_en.arb`, `app_en_US.arb`, `intl_zh_Hans_CN.arb`, and one with `@@locale` that disagrees with the name (the header wins).

<a id="fmt-09"></a>
### FMT-09 — YAML: flat dotted keys become nested; `__self` is written into the file

- **Severity:** Low · **Area:** YAML · **Checked:** reproduced · **Code:** `YamlSaveStrategy.cs:56-79,99-103`
- **Repro:** items `app.title`, `app` (its own value) and `a.b.c`. Save.
- **Actual file:**
  ```yaml
  a:
    b:
      c: x
  app:
    __self: Self
    title: T
  ```
  Toucan loads it back to the same three keys, so the round trip inside Toucan is lossless.
- **What is still wrong:**
  - A project whose YAML uses flat dotted keys (`"a.b.c": x`, i18next with `keySeparator: false`) is rewritten as nested maps. The app reading it then finds nothing.
  - A key that is both a value and a parent writes a `__self:` child that no other tool understands.
- **Fix direction:** remember per file whether keys were flat or nested and write the same style. For the parent-and-child clash, warn and keep the nested key instead of inventing `__self`.
- **Also noticed, not run:** `EscapeYamlValue` (`YamlSaveStrategy.cs:116-148`) quotes `yes`/`no`/`true`/`false`/`null` but not `on`, `off`, `~`, or number-like text such as `1.0` or `007`. A YAML 1.1 reader would turn those into booleans or numbers. Worth a test before changing anything.
- **Tests to add:** flat dotted file stays flat; nested stays nested; clash case.

---

## Release and distribution

<a id="rel-01"></a>
### REL-01 — No CI or release pipeline

- **Severity:** Medium · **Checked:** there is no `.github/`, no workflow and no other CI config in the repo.
- **State:** release builds come from `publish.ps1`, `packaging/build-macos-app.sh` and `dotnet publish` run by hand. v0.18.0 Linux tarballs were made by hand too. The test suites (`Toucan.Core.Tests`, `Toucan.Avalonia.Tests`) are not run automatically on pull requests.
- **Impact:** no gate on regressions, and releases are only as reproducible as one machine.
- **Fix direction:** FG-01 and FG-16 in [docs/todos/future-roadmap.md](todos/future-roadmap.md): a PR workflow (build and test on macOS, Linux, Windows with `Toucan.CrossPlatform.slnx`) first, then a tag-triggered release workflow that builds all packages and attaches them to the GitHub release.

<a id="rel-02"></a>
### REL-02 — macOS app is not notarized

- **Severity:** Medium · **Checked:** README and `packaging/build-macos-app.sh`.
- **State:** the build is ad-hoc signed. The script already signs with a Developer ID and notarizes when `SIGN_IDENTITY` and `NOTARY_PROFILE` are set, but no such identity is configured.
- **Impact:** Gatekeeper blocks the first launch ("could not verify"). Users must use Open Anyway or clear the quarantine flag. This is documented in the README but costs installs.
- **Fix direction:** an Apple Developer ID and a notary profile stored as CI secrets (needs REL-01).

<a id="rel-03"></a>
### REL-03 — No auto-updater and no update check

- **Severity:** Medium · **Checked:** no update or channel code exists in `Toucan.Avalonia` or `Toucan.Core`, and the Settings dialog has no update page.
- **Correction:** the older version of this file said "the About page shows the settings". That described the removed WPF app. In the Avalonia app there is no update setting at all.
- **Impact:** users stay on old builds, which matters while the formats above lose data.
- **Fix direction:** FG-02, planned for v0.24: version check against the GitHub Releases API, a notify-only mode first, then download and apply. Depends on REL-01 and signing (REL-02).

<a id="rel-04"></a>
### REL-04 — Windows has no installer

- **Severity:** Low · **Checked:** README says "portable x64 zip; no installer yet".
- **State:** `installer.iss` (Inno Setup) and `packaging/Build-Msix.ps1` exist and refer to `Toucan.exe`, which is the Avalonia app's assembly name. I did not check whether they still work with the Avalonia build since the switch from WPF.
- **Fix direction:** run both against the current build, fix what breaks, then publish an installer with each release (part of REL-01).

---

## App gaps

<a id="app-01"></a>
### APP-01 — Source Control and Dictionary panels do not exist

- **Severity:** Low · **Checked:** `App.RegisterSidePanels` registers Explorer, Search, Issues, Source Code, Languages, Inspector, Translation, Memory. No panel for Git or glossary.
- **Correction:** the old text also listed "Translation" as missing. It exists (`machine-translation`, titled Translation).
- **Plan:** Git integration is FG-06 (v0.23), the glossary is FG-15 (v0.21).

<a id="app-02"></a>
### APP-02 — Shortcuts cannot be changed

- **Severity:** Low · **Checked:** Settings → Shortcuts lists `KeybindingService.GetDefinitions()` with no edit control and nothing is stored.
- **Fix direction:** per-user overrides in `settings.json`, a conflict check, a reset button, and the command palette showing the active shortcut (it already reads the definitions).

<a id="app-03"></a>
### APP-03 — No notification history

- **Severity:** Low · **Checked:** `StatusBarService.ShowNotificationBadge` is only fed the number of empty translations (`MainWindowViewModel.cs:266`). There is no `NotificationService` and nothing opens when the badge is clicked.
- **Fix direction:** a small service holding title, message, severity and time, a flyout anchored to the badge, and producers: save failures, validation summaries, plugin load errors, and (later) update availability.

---

## Quality

<a id="qa-01"></a>
### QA-01 — Missing regression tests

- **Severity:** Medium · **Checked:** from code. I found no test that targets these, so they could regress unnoticed.
- **Bugs fixed without a test:** `DiffMergeEngine` baselines (merged items stayed dirty), `AutoSaveService` dispose during a save, `TranslationManagementService` double `DirtyStateChanged`, iOS `.strings` `\\n` handling, Java `.properties` line continuation. The last two I re-ran by hand on 2026-10-07 and they behave correctly.
- **Also missing:** every case in FMT-01 to FMT-09. `FormatRoundTripTests` only covers simple keys and values (`app.title=Hello`).
- **Fix direction:** one test per bug with the exact input from this file. This is the cheapest item here and protects the fixes above.

<a id="qa-02"></a>
### QA-02 — Performance not profiled

- **Severity:** Info · **Checked:** no. Loading depth, paging and `MaxItems` exist as mitigations, but there are no measurements for projects with tens of thousands of keys. Profile open, search and save before 1.0.

---

## Where the old fixes went

The earlier version of this file kept tables of fixed bugs (B1 to B11 and the v0.14 to v0.16 sets). All of them are already in [CHANGELOG.md](../CHANGELOG.md), so the tables were removed here.

| Old ID | What was fixed | Changelog entry |
|--------|----------------|-----------------|
| B1 | DiffMergeEngine merged items stayed dirty | 0.17.1 |
| B2 | AutoSaveService crash when disposed during a save | 0.17.1 |
| B3 | Double `DirtyStateChanged` (TOCTOU) | 0.17.1 |
| B4 | External reload updated UI collections off the UI thread | 0.17.1 |
| B5 | iOS `.strings` `\\n` corrupted | 0.17.1 |
| B6 | Java `.properties` line continuations truncated values | 0.17.1 |
| B7 to B11 | WPF-only UI fixes (Issues grouping, Search panel sizing, Source Code panel, Explorer foreground, status bar clicks) | 0.17.2 |
| v0.14.1 to v0.16.1 | Earlier bug batches | 0.14.1, 0.14.2, 0.15.0, 0.16.1 |

B7 to B11 were fixed in the WPF app. Whether the Avalonia app has the same problems was not checked in this audit.
