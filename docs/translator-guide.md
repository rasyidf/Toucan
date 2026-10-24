---
title: "Toucan for Professional Translators"
status: active
updated: 2026-10-25
summary: "Dedicated guide for professional translators and localization specialists: CAT-style workflows, translation memory, metrics, quality checks, and client exchange."
---
# Toucan for Professional Translators

Software localization often forces linguists to juggle unfamiliar developer formats (JSON, YAML, PO, RESX, ARB, XLIFF), where accidental syntax errors or broken placeholders can cause application crashes.

**Toucan** bridges the gap between software engineering and professional Computer-Assisted Translation (CAT). It provides a familiar, distraction-free environment with Translation Memory, quality assurance, live metrics, and keyboard-driven efficiency—while handling file formatting, encodings, and persistence under the hood.

---

## 1. Source-First Workspace & Productivity Metrics

### Two-Column Segment Layout
In wide windows, Toucan formats translation cards with a dedicated **source-first two-column layout**:
- **Left Column:** Source language text, locked against accidental overwrites.
- **Right Column:** Active target language input field.
- **Above:** Clean key path, developer comments, and context notes.

In compact or split-screen windows, cards smoothly stack vertically while maintaining clear visual distinction between source and target text.

### Live Character and Word Counts
Accurate metrics are critical for software UI constraints (e.g. character limits on buttons, banners, or mobile screens) as well as translation billing:
- When a target field is focused, Toucan displays live **Unicode scalar character counts** and **whitespace-delimited word counts** directly in the card header.
- Counters update in real time as you type, helping you avoid UI text truncation before the code is built.

---

## 2. Translation Memory (TM) & Segment Reuse

Toucan incorporates a local, high-performance Translation Memory powered by a trigram fuzzy-matching engine.

### Inline Ghost Suggestions
When you navigate to an empty target segment:
1. Toucan searches your Translation Memory in the background.
2. If a match meets your configured threshold (e.g., 75% similarity), the suggested translation appears directly inside the input box as **faint ghost text**.
3. Press <kbd>Tab</kbd> to accept the suggestion instantly.
4. Continue typing to dismiss or adapt the suggestion.

### Suggestion Provenance
Suggestions in the Inspector panel clearly identify their origin:
- **100% Exact Match:** Identical source text from a previous verified project.
- **Fuzzy Match (%):** Close match with similarity percentage and highlighted diff.
- **MT / AI:** Clearly labeled when sourced from an optional machine translation provider.

### TMX Import & Export
You are never locked into Toucan. You can import existing industry-standard **Translation Memory eXchange (`.tmx`)** files or export your project's memory to share with other CAT tools (SDL Trados, memoQ, Phrase, Lokalise, etc.). Go to **Tools → Translation Memory** to manage stored units, set matching thresholds, or clear segments.

---

## 3. Keyboard-Driven Review & Approval

Linguists translate fastest when their hands never leave the home row. Toucan’s review workflow is built around continuous typing:

### Rapid Review Cycle
- Press <kbd>Ctrl/Cmd+Enter</kbd> (**Approve and Next**): Marks the current segment as **Approved**, saves the translation, and immediately advances focus to the next target field.
- **Retained Target Focus:** Focus does not jump to checkboxes, toolbars, or scrollbars; your cursor stays inside the text box across card boundaries and pagination pages, allowing you to review dozens of strings per minute without touching the mouse.
- Press <kbd>Ctrl/Cmd+Shift+Enter</kbd> (**Next Pending**): Skips already-approved segments and jumps directly to the next segment needing your attention.

### Review Mode
Click **Review** in the top navigation to filter your entire workspace down to unapproved, draft, or flagged items. This strips away completed work and allows you to focus exclusively on quality verification.

### Grouped Bulk Undo
If you perform a bulk operation (such as accepting pre-translation for a namespace, running Replace All on a terminology change, or approving a batch of strings), Toucan records the entire operation as a **single grouped undo step**. Pressing <kbd>Ctrl/Cmd+Z</kbd> restores all previous values, comments, and review timestamps in one action.

---

## 4. Linguistic QA & Syntax Protection

Software strings contain code variables, markup, and plurals. Breaking these causes runtime bugs. Toucan protects strings automatically:

### Placeholder & Variable Shields
Toucan detects and safeguards common software interpolation patterns:
- Named variables: `{{username}}`, `{count}`, `:user_id`, `${name}`
- Printf formats: `%s`, `%d`, `%1$s`, `%.2f`
- XML / HTML tags: `<a href="...">`, `<b>`, `<br/>`

The **Issues** panel flags any segment where target placeholders do not match the source string.

### ICU Plurals & Complex Messages
For formats supporting complex plural forms (i18next, Flutter ARB, ICU MessageFormat):
- Standard plural suffixes (`_one`, `_other`, `_few`, `_many`, `_zero`) are grouped together logically in the tree.
- Complex ICU expressions (`{count, plural, one {# day} other {# days}}`) can be edited as whole strings with syntax validation, preventing syntax corruption.

### Integrated Spell Checking
- Built-in spell checking runs completely offline using the trusted **Hunspell** engine.
- Misspelled words are indicated with squiggly red underlines.
- Right-click or press the context menu key to choose suggestions, ignore the term, or add it to your project-specific or personal dictionary.
- Placeholders, code markup, and technical tokens are automatically excluded from spell checking.

---

## 5. Client & Team Review via Excel Spreadsheets

Not every client or subject matter expert has Toucan installed. Toucan makes offline review seamless through round-trip spreadsheet interchange:

### Exporting for External Review
1. Choose **File → Export → Excel (.xlsx)…**
2. Select your dialect:
   - **Toucan Dialect:** Clean layout with Key, Source, Target languages, Comments, and Per-Language Approval columns.
   - **BabelEdit Dialect:** Compatible column layout for teams migrating from or collaborating with BabelEdit users.
3. Send the `.xlsx` file to clients or proofreaders. They can edit translations, update comments, or check approval checkboxes in Microsoft Excel, Apple Numbers, or Google Sheets.

### Importing & Reconciling Spreadsheets
1. Choose **File → Import → Excel (.xlsx)…**
2. Toucan inspects the workbook and displays an **interactive diff preview**:
   - Lists new strings, modified translations, comment changes, and approval toggles.
   - Diagnoses any header mismatches or unrecognized columns before changing your project.
3. Review the preview and click **Apply Changes**. All spreadsheet edits merge into your native project with one-step undo support.

---

## 6. Complete Data Privacy & Security

Confidentiality is a legal requirement for professional translators working under NDAs:

- **Offline-First:** Toucan does not require an internet connection or cloud account to edit, validate, search, or save translation files.
- **Zero Telemetry on Strings:** Your translation content is never uploaded, tracked, or used for model training.
- **Opt-In Machine Translation & AI:** All machine translation (Google, DeepL, Microsoft, Claude, OpenAI, Gemini) is **strictly off by default**. It only runs when you explicitly configure an API key and trigger a translation request.
- **Local Private LLM Support:** For sensitive projects under strict NDAs, you can connect Toucan to a local, air-gapped AI server (such as Ollama or LM Studio) running models entirely on your own computer.
- **Operating-System Key Encryption:** All provider and AI API keys are stored in your operating system's native encrypted credential store (Windows DPAPI, macOS/Linux AES-GCM with protected user key), never in plaintext or in your project folder.

---

## 7. Universal Format Support

Toucan eliminates the need to maintain different editors for different programming languages:

| Ecosystem | Supported File Formats |
|---|---|
| **Web & JavaScript** | JSON (flat & namespaced/i18next), FormatJS catalogs, YAML |
| **Mobile (iOS & Android)** | iOS `.strings` (UTF-16 & UTF-8), Android `strings.xml` |
| **Flutter / Dart** | Flutter ARB (`intl_*.arb`, `app_*.arb`, `@key` metadata) |
| **.NET / Windows** | RESX (`Resources.*.resx`) |
| **GNU / Linux / PHP** | GNU Gettext PO (`.po`), Laravel PHP arrays (`lang/*.php`) |
| **Cross-Platform / Localization** | XLIFF 1.2 & 2.0 (`.xlf`), CSV, TSV, Java `.properties` |
| **Migration** | BabelEdit (`.babel`) project folders |

Each format loader and saver preserves original file encodings (UTF-8 with or without BOM, ISO-8859-1 Latin-1) and line endings (LF, CRLF), ensuring that commits pass continuous integration and code review without unwanted formatting churn.
