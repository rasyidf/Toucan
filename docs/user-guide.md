---
title: "Toucan User Guide"
status: active
updated: 2026-10-25
summary: "Comprehensive guide to using Toucan: opening projects, editor modes, keyboard workflows, validation, machine translation, and safe saving."
---
# Toucan User Guide

Toucan is a cross-platform desktop editor designed for software translation and localization files. It runs natively on Windows, macOS, and Linux, providing a unified workspace to translate, review, and validate over 14 resource formats without switching between text editors or risking file corruption.

---

## 1. Getting Started

### Opening a Project
Toucan works with your existing project directory:
1. **Drop a folder:** Drag and drop your project folder onto the Toucan welcome screen, or choose **File → Open Folder…** (`Ctrl/Cmd+O`).
2. **Framework auto-detection:** Toucan automatically detects common localization frameworks (including i18next, Flutter, Android, iOS, .NET RESX, Rails, Gettext PO, and Laravel) and configures the translation layout and language files for you.
3. **BabelEdit import:** If your folder contains a `.babel` project, Toucan detects it and offers to import its configuration, package mappings, descriptions, comments, and approval states into a native `toucan.tproj` manifest without altering the original `.babel` file.

### First-Run Setup & Guide
On first launch, Toucan presents a lightweight onboarding dialog where you can set your preferred source language (such as `en` or `en-US`), select your interface theme (System, Light, Dark, or custom accent colors), and optionally configure AI translation assistance. You can reopen the editing guide at any time from **Help → Editing Guide**.

---

## 2. Workspace Overview

The Toucan window consists of three main regions:

```
┌────────────────────────────────────────────────────────────────────────┐
│ Title Bar: Logo · App Mode (Editor/Review/Audit) · Palette · Window    │
├───────────────┬───────────────────────────────────────┬────────────────┤
│ Activity Bar  │ Translation Grid / Card View          │ Inspector      │
│ & Sidebar     │                                       │                │
│               │ [Key: app.title]                      │ · Statistics   │
│ · Explorer    │ Source (en): Hello World              │ · TM Matches   │
│ · Search      │ Target (es): [Hola Mundo            ] │ · Comments     │
│ · Issues      │                                       │ · Validation   │
│ · Source Code │ [Key: app.welcome]                    │                │
│ · Languages   │ Source (en): Welcome to Toucan        │                │
│               │ Target (es): [Bienvenido a Toucan   ] │                │
├───────────────┴───────────────────────────────────────┴────────────────┤
│ Status Bar: Project · Language · Filter · Notifications · Line Endings │
└────────────────────────────────────────────────────────────────────────┘
```

- **Activity Bar & Sidebar:** Switch between the **Explorer** (file/namespace hierarchy), **Search** (full regex search & replace), **Issues** (real-time validation findings), **Source Code** (code reference scanner), and **Languages** (manage project locales).
- **Translation Editor:** View strings either in a paged grid or infinite scroll. In wide windows, translation cards present a comfortable **two-column layout** with source on the left and target on the right. In narrower windows, cards automatically stack.
- **Inspector Panel:** Displays metadata for the focused translation segment, including character/word counts, developer comments, translation memory suggestions, and validation warnings.
- **Status Bar:** Shows current project information, encoding (UTF-8, BOM, Latin-1), line endings (LF, CRLF), and a **Notification History** flyout.

---

## 3. Editing Modes

Toucan provides dedicated modes in the top navigation bar to match different stages of your localization workflow:

### Editor Mode
Your primary working environment. All translated and untranslated strings are displayed. Focusing an empty target field automatically searches Translation Memory (TM) and displays the best matching suggestion as subtle ghost text. Press <kbd>Tab</kbd> to accept it immediately.

### Review Mode
Designed for quality assurance and verification. Review Mode filters your view to items that are either untranslated, unapproved, or contain validation warnings. As you verify each segment, toggle its approval state or press <kbd>Ctrl/Cmd+Enter</kbd> to approve and move to the next item.

### Audit Mode
A strictly read-only inspection mode. All editing fields, buttons, and mutation shortcuts are disabled to prevent accidental modifications while inspecting translation history, author metadata, and approval timestamps.

### Zen Mode (<kbd>F11</kbd>)
Hides the sidebar, inspector, toolbar, and status bar, leaving only your translation cards in a focused view. Navigate smoothly between strings using <kbd>J</kbd> / <kbd>K</kbd> or <kbd>Ctrl/Cmd+Enter</kbd>.

---

## 4. Keyboard-Driven Workflow

Toucan is engineered for touch-typists. Routine editing and review require no mouse interaction:

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl/Cmd+Enter</kbd> | **Approve and Next** | Marks current translation approved and moves focus to the next target field. |
| <kbd>Ctrl/Cmd+Alt+→</kbd> | **Next Pending** | Jumps focus to the next untranslated or unapproved field. |
| <kbd>Tab</kbd> | **Accept Suggestion** | Accepts the ghost-text translation memory suggestion into an empty field. |
| <kbd>Ctrl/Cmd+Shift+P</kbd> | **Command Palette** | Search and run any menu item, command, or plugin action. |
| <kbd>Ctrl/Cmd+/</kbd> | **Shortcut Sheet** | Displays a searchable reference of all keyboard shortcuts. |
| <kbd>Ctrl/Cmd+S</kbd> | **Save Project** | Saves all modified files atomically. |
| <kbd>Ctrl/Cmd+Z</kbd> / <kbd>Y</kbd> | **Undo / Redo** | Full history navigation. Bulk actions undo as a single step. |
| <kbd>F11</kbd> | **Zen Mode** | Toggle distraction-free full-window editing. |

Shortcuts can be customized in **Settings → Keyboard Shortcuts**, with automatic conflict detection and one-click reset to defaults.

---

## 5. Quality Assurance & Validation

Toucan continuously checks your work as you type to prevent broken software releases:

- **Placeholder Protection:** Detects mismatches in interpolation variables, including `{{var}}`, `{0}`, `%s`, `:param`, and ICU syntax.
- **Missing & Empty Translations:** Flags untranslated keys, intentional empty strings, or untranslated copies (where target equals source text).
- **Whitespace & Markup:** Catches missing leading/trailing whitespace, line-break mismatches, and broken HTML/XML tags.
- **Spell Checking:** Powered by the offline Hunspell engine with user-managed dictionaries in **Settings → Options**. Misspelled words receive squiggly underlines with right-click suggestions.
- **Issues Panel:** Displays all project findings in one place. Double-clicking any issue immediately jumps to and focuses the offending segment.

Validation findings never block you from saving drafts, but strict policies (such as "Require valid translations to approve") can be enabled in **Project Properties → Saving**.

---

## 6. Pre-Translation & Machine Translation

Pre-translate single keys, entire namespaces, or all project languages using Google Translate, DeepL, Microsoft Translator, or AI:

1. Select **Tools → Pre-translate…**
2. Choose your target language, scope, and translation provider.
3. **Dry-Run Preview:** Toucan queries the provider and displays a complete side-by-side diff table of suggested translations without modifying your workspace.
4. **Placeholder Safety:** Formatting tags and variables are shielded before sending and restored after translation.
5. Click **Apply Preview** to commit the translations into your workspace with full undo support.

API keys are stored securely in your operating system's native encrypted keystore (DPAPI on Windows, AES-GCM on macOS/Linux), never in your project files or version control.

---

## 7. Bulk Operations

Select multiple keys in the tree or translation list to perform batch operations:
- **Bulk Approve / Reject:** Mark hundreds of translations verified at once.
- **Bulk Pre-translate:** Query providers for all selected keys in one pass.
- **Move to Namespace:** Organize translation keys into modules or folders.
- **Search & Replace:** Replace terminology across keys, values, or comments with full regex support.
- **Grouped Bulk Undo:** Every batch operation commits as a single undo step. Pressing <kbd>Ctrl/Cmd+Z</kbd> rolls back the entire batch, restoring all previous values, comments, and review timestamps.

---

## 8. Safe Saving & Recovery

Toucan protects your localization files from corruption and data loss:
- **Atomic File Replacement:** Files are written to temporary files, flushed, and swapped in atomically. A sudden crash or power outage never leaves a half-written file.
- **Journaled Rollback (`SaveTransaction`):** In multi-file projects, if saving one file fails (e.g. disk full or locked file), all previously written files in that save operation are automatically rolled back.
- **Crash Recovery Drafts:** Unsaved edits are backed up periodically to a recovery directory. If Toucan closes unexpectedly, reopening the project prompts you to restore your unsaved drafts.
- **External Change Detection:** If a teammate or Git pull modifies translation files while Toucan is open, Toucan detects the change and provides an interactive merge dialog to resolve any conflicts without overwriting disk contents.
