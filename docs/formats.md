---
title: "Format support matrix"
status: active
summary: "Which file formats Toucan edits safely, what survives a save, and what does not."
---
# Format support matrix

Generated from the format strategies by `FormatSupportTests`; to refresh it run the Core tests with `TOUCAN_UPDATE_DOCS=1`. Do not edit by hand.

**Full**: everything listed under *Kept* survives load and save. **Limited**: strings are editable, but the items under *Not supported* are dropped or rewritten on save. 
Where a format can lose content that is already in the project's files (Android XML and RESX), Toucan checks the files when the project opens and again before saving: it warns, and refuses to overwrite them. Use Save As to write a copy.

| Format | ID | Editing | Versions | Kept | Not supported |
|---|---|---|---|---|---|
| Android XML | `android-xml` | Limited | Android res/values\*/strings.xml | &lt;string&gt; and &lt;string-array&gt; items<br>multiline text<br>markup characters | &lt;plurals&gt;, other resource types and translatable="false" are not kept: such projects open with a warning and cannot be saved in place<br>Android escapes (\' and \n) are not converted<br>XML comments are not kept |
| CSV | `csv` | Full | RFC 4180 CSV: key column plus one column per language | Quoted fields<br>embedded commas<br>quotes and line breaks | Only the key and language columns are kept; extra columns are dropped |
| Flutter ARB | `arb` | Full | Flutter ARB (intl_\*.arb, app_\*.arb), region and script locales | @key metadata (description, placeholders)<br>@@ header entries<br>ICU plural and select messages kept as text<br>@@locale | ICU messages are edited as one string, not as separate plural forms |
| Gettext PO | `po` | Full | GNU gettext PO | msgctxt<br>plural forms<br>translator and extracted comments<br>references<br>flags (fuzzy)<br>header | Obsolete (#~) entries are not read and are dropped on save<br>POT templates are not created automatically |
| INI | `ini` | Limited | INI sections | Export only: flat key=value lines, values with special characters quoted | Cannot be opened again by Toucan (no loader)<br>Dots in keys are written as underscores<br>Sections and comments are not written |
| JSON (flat) | `json` | Full | Any JSON object; nested objects and arrays | Nested keys<br>arrays<br>multiline text<br>escapes<br>numbers and booleans left unedited<br>placeholders<br>sorted stable output | Comments (JSON has none)<br>null values are dropped<br>Empty values are not written<br>Key order is sorted on save |
| JSON (namespaced / i18next) | `namespaced` | Limited | One JSON file per namespace under locales/&lt;lang&gt;/ | Nested keys<br>multiline text<br>escapes<br>numbers and booleans left unedited | Writes both &lt;lang&gt;.json and locales/&lt;lang&gt;/&lt;ns&gt;.json, so a reload sees each key twice<br>null values are dropped<br>Key order is sorted on save |
| Java .properties | `java-properties` | Full | Java .properties (ISO-8859-1 with \uXXXX escapes); line continuations | Escapes<br>unicode<br>leading spaces<br>line continuations<br>multiline text | Comments in the file are not kept<br>Key order is sorted on save |
| Laravel PHP | `laravel-php` | Limited | Laravel lang/&lt;locale&gt;/&lt;file&gt;.php returning an array (short and array() syntax) | Nested arrays<br>single- and double-quoted strings<br>multiline text<br>escapes<br>placeholders (:name) | Computed values (function calls, constants, numbers) are not read and are dropped on save<br>Comments and PHP code around the array are not kept<br>JSON language files (lang/en.json) are a separate format |
| RESX | `resx` | Limited | .resx and .resw string resources | String entries<br>multiline text<br>culture suffix in the file name | Non-string resources, metadata and custom headers: such projects open with a warning and cannot be saved in place<br>Files not named Resources.resx or Resources.&lt;culture&gt;.resx: blocked for the same reason<br>Per-entry comments are not kept |
| TOML | `toml` | Limited | TOML basic and literal strings; [section] and dotted keys; one file per language | Sections<br>dotted keys<br>multiline text via escapes<br>unicode escapes | Comments in the file are not kept<br>Arrays, inline tables, multiline (""") strings and dates are not read |
| XLIFF | `xliff` | Full | XLIFF 1.2 and 2.0 | Source text<br>notes<br>state<br>datatype<br>original<br>extra elements such as context-group<br>original file paths | Segmentation (&lt;seg-source&gt;, &lt;mrk&gt;) is not edited |
| YAML | `yaml` | Limited | YAML 1.1 and 1.2 scalar maps; one file per language | Nested and flat dotted keys (same style as the file)<br>block scalars (\| and &gt;) read<br>multiline text<br>quoted scalars such as on<br>off<br>~ and numbers | Comments in the file are not kept<br>Anchors, aliases, tags and sequences are not read<br>Block scalars are written as quoted strings |
| iOS .strings | `ios-strings` | Limited | Apple .strings ("key" = "value";) | Escapes<br>unicode<br>multiline text<br>per-language .lproj folders | .stringsdict plural files are not read<br>Comments in the file are not kept |
