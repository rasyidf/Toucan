# Toucan.Core — Modularization & Framework Standardization Plan

## Problem Statement

Currently, a "framework" (i18next, Android, Flutter, .NET, etc.) is mapped directly to a `SaveStyles` enum, which picks one Load/Save strategy. This conflates **file format** with **project structure conventions**. In reality:

- **i18next** = nested JSON + folder convention (`locales/{lang}/{namespace}.json`) + plural suffixes (`_one`, `_other`)
- **Android** = XML format + folder convention (`res/values-{lang}/strings.xml`) + plurals via `<plurals>`
- **Flutter ARB** = JSON with `@` metadata keys + `l10n.yaml` manifest
- **.NET RESX** = XML format + assembly embedding + satellite assemblies
- **BabelEdit** = metadata-only project file (`.babel` XML) + any underlying format
- **Paraglide** = compiled i18n with `messages/{lang}.js` + type-safe codegen manifest

Each framework has three independent concerns:
1. **File Format** — how to read/write the translation file (JSON, XML, YAML, etc.)
2. **Project Layout** — folder conventions, file naming, language detection from paths
3. **Metadata** — plural rules, context markers, approval state, description, max-length, platform-specific annotations

---

## Proposed Architecture

### Layer 1: File Format (already exists)

```
ILoadStrategy / ISaveStrategy
```

Pure format parsers. They know how to turn bytes into `TranslationItem[]` and back. **No framework knowledge.** This layer is done and correct.

### Layer 2: Framework Profile (NEW — the missing abstraction)

```csharp
public interface IFrameworkProfile
{
    string Id { get; }                    // "i18next", "android", "flutter-arb", "dotnet-resx", etc.
    string DisplayName { get; }           // "i18next (JSON)"
    SaveStyles DefaultFormat { get; }     // The file format this framework uses
    
    // --- Project Layout ---
    /// <summary>Discover translation files in a folder using this framework's conventions.</summary>
    IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder);
    
    /// <summary>Determine language from file path (e.g., "locales/fr/common.json" → "fr").</summary>
    string? ExtractLanguage(string relativePath);
    
    /// <summary>Generate the output path for a language file.</summary>
    string GetFilePath(string rootFolder, string language, string? package = null);
    
    // --- Metadata ---
    /// <summary>Extract framework-specific metadata from raw content (plurals, descriptions, etc.).</summary>
    FrameworkMetadata ExtractMetadata(string content, string language);
    
    /// <summary>Inject metadata back when saving (e.g., ARB @-keys, XML comments).</summary>
    string InjectMetadata(string content, FrameworkMetadata metadata);
    
    // --- Validation ---
    /// <summary>Framework-specific validation rules.</summary>
    IEnumerable<IValidationRule> GetValidationRules();
    
    // --- Plural/Gender ---
    PluralConfig? GetPluralConfig();
}

public record DiscoveredFile(string Path, string Language, string? Package);

public class FrameworkMetadata
{
    public Dictionary<string, string> Descriptions { get; set; } = [];    // key → description
    public Dictionary<string, int> MaxLengths { get; set; } = [];        // key → max chars
    public Dictionary<string, string[]> PluralForms { get; set; } = [];  // key → required suffixes
    public Dictionary<string, string> Context { get; set; } = [];        // key → translator context
}
```

### Layer 3: Project Service (refactored)

```csharp
public interface IProjectService
{
    ProjectLoadResult LoadProject(string folder);
    ProjectLoadResult LoadProject(string folder, IFrameworkProfile profile);  // explicit framework
    void Save(ProjectSettings project, IEnumerable<TranslationItem> translations);
    IFrameworkProfile DetectFramework(string folder);  // auto-detect from folder structure
}
```

The project service orchestrates: `IFrameworkProfile` → `ILoadStrategy` → `TranslationItem[]`.

### Layer 4: Validation Pipeline (NEW)

```csharp
public interface IValidationRule
{
    string Id { get; }
    string Name { get; }
    ValidationSeverity Severity { get; }
    IEnumerable<ValidationResult> Validate(ValidationContext context);
}

public class ValidationContext
{
    public required IEnumerable<TranslationItem> Items { get; init; }
    public required ProjectSettings Settings { get; init; }
    public IFrameworkProfile? Profile { get; init; }
}

public interface IValidationPipeline
{
    IEnumerable<ValidationResult> RunAll(ValidationContext context);
    IEnumerable<ValidationResult> Run(ValidationContext context, IEnumerable<string> ruleIds);
}
```

Runs pre-save, post-load, or on-demand. Framework profiles contribute their own rules.

### Layer 5: Source Control (NEW)

```csharp
public interface ISourceControlService
{
    bool IsRepository(string folder);
    string? GetCurrentBranch(string folder);
    IEnumerable<FileChange> GetStatus(string folder);
    IEnumerable<TranslationDiff> DiffSinceCommit(string folder, string commitish = "HEAD");
    void Stage(string folder, IEnumerable<string> relativePaths);
    void Commit(string folder, string message);
}
```

Implemented via `git` CLI subprocess calls (no libgit2 dependency needed).

---

## Framework Profiles — What's Different Per Framework

| Framework | Format | File Discovery Pattern | Language Extraction | Special Metadata |
|-----------|--------|----------------------|--------------------|--------------------|
| **i18next** | Nested JSON | `locales/{lang}/{ns}.json` or `{lang}.json` | Folder name or filename | Plural suffixes `_one/_other/_many`, interpolation `{{var}}` |
| **React (react-intl)** | Flat JSON | `src/translations/{lang}.json` | Filename | ICU MessageFormat plurals |
| **Vue (vue-i18n)** | JSON/YAML | `src/locales/{lang}.json` or SFC `<i18n>` blocks | Filename | Nested + linked messages `@:key` |
| **Angular** | JSON or XLIFF | `src/locale/messages.{lang}.xlf` or `assets/i18n/{lang}.json` | Filename suffix | ICU in XLIFF, `meaning`/`description` attributes |
| **Flutter (ARB)** | ARB (JSON) | `lib/l10n/app_{lang}.arb` | Filename suffix | `@key` metadata (description, placeholders, plural) |
| **Laravel** | PHP arrays or JSON | `lang/{lang}/{file}.php` or `lang/{lang}.json` | Folder name | Nested arrays, `:param` placeholders |
| **.NET (RESX)** | RESX XML | `Resources/{Name}.{lang}.resx` | Filename suffix before `.resx` | `<comment>` element, `type` attribute |
| **Android** | XML | `res/values-{lang}/strings.xml` | Folder suffix `values-{lang}` | `<plurals>`, `<string-array>`, `translatable="false"` |
| **iOS** | .strings | `{lang}.lproj/Localizable.strings` | Folder name before `.lproj` | `/* comment */` before entry |
| **Ruby/Rails** | YAML | `config/locales/{lang}.yml` | Root key in YAML or filename | Nested, plurals via `one:/other:` keys |
| **Gettext (PO)** | PO/POT | `locale/{lang}/LC_MESSAGES/{domain}.po` | Folder name | `msgctxt`, `#. comment`, plural forms header |
| **Java** | .properties | `messages_{lang}.properties` | Filename suffix | ISO-8859-1, `# comment` lines |
| **Paraglide** | JSON/TS | `messages/{lang}.json` | Filename | Compiled output, type manifest |
| **BabelEdit** | Any (metadata-only project) | Defined in `.babel` XML manifest | From manifest | Approval flags, descriptions, packages |
| **Toucan native** | Any | Defined in `toucan.project` JSON manifest | From manifest | Approval, comments, packages |

**Key insight:** Format ≠ Framework. Multiple frameworks use JSON, but differ in:
- Where files live (discovery pattern)
- How language codes are encoded in paths
- What metadata lives alongside translations
- What plural/interpolation syntax is expected

---

## Current vs Proposed Flow

### Current (simplified):
```
User selects "i18next" → SaveStyles.Namespaced → NamespacedLoadStrategy.Load(folder)
                                                  ↑ just scans *.json files in folder
```

### Proposed:
```
User selects "i18next" → I18nextProfile.DiscoverFiles(folder)
                          → finds locales/en/common.json, locales/fr/common.json
                          → extracts language from path ("en", "fr")
                          → calls NestedJsonLoadStrategy for each file
                          → extracts metadata (@descriptions, plural markers)
                          → returns TranslationItem[] + FrameworkMetadata

On Save:
  → I18nextProfile.GetFilePath(folder, "fr", "common") → "locales/fr/common.json"
  → I18nextProfile.InjectMetadata(json, metadata) → adds @-keys back
  → NestedJsonSaveStrategy.Save(path, context)
```

---

## Implementation Plan

### Phase A: Framework Profiles (refactor, no breaking changes)

1. **Define `IFrameworkProfile` interface** in `Toucan.Core/Contracts/`
2. **Create profile implementations** (start with 5 most-used):
   - `I18nextProfile` — nested JSON, folder-based language, plural suffixes
   - `AndroidProfile` — XML, `values-{lang}` folders
   - `FlutterArbProfile` — ARB with `@` metadata, `l10n.yaml` detection
   - `DotNetResxProfile` — RESX, `Name.{lang}.resx` convention
   - `GenericJsonProfile` — flat JSON fallback (current behavior)
3. **Refactor `ProjectModeResolver`** → use `IFrameworkProfile.DiscoverFiles()` for detection
4. **Refactor `ProjectService.LoadProject`** → delegate to profile for discovery + metadata
5. **Map `FrameworkTile.Style` → `IFrameworkProfile`** instead of raw `SaveStyles`

### Phase B: Validation Pipeline

1. **Define contracts** (`IValidationRule`, `IValidationPipeline`, `ValidationContext`)
2. **Built-in rules**: MissingTranslation, PlaceholderMismatch, DuplicateKey, EmptyValue, MaxLength
3. **Framework-contributed rules** via `IFrameworkProfile.GetValidationRules()`
4. **Wire into save flow** — run validation before write, surface warnings in UI
5. **Wire into pre-translate flow** — validate results before committing

### Phase C: Source Control

1. **Define `ISourceControlService`** interface
2. **Implement `GitSourceControlService`** — wraps `git` CLI
3. **UI integration**: status bar shows branch, dirty indicator shows git diff count
4. **Save hook**: optionally auto-stage changed translation files

### Phase D: Module Extraction (optional, when complexity warrants it)

```
Toucan.Core/                 ← Contracts + Models only (~10 files)
Toucan.Core.Formats/         ← LoadStrategies + SaveStrategies
Toucan.Core.Providers/       ← Translation providers
Toucan.Core.Frameworks/      ← IFrameworkProfile implementations
Toucan.Core.Validation/      ← Pipeline + rules
Toucan.Core.SourceControl/   ← Git integration
```

---

## Data Flow (Final Architecture)

```
┌─────────────────────────────────────────────────────────────┐
│                       ProjectService                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Load:  Profile.DiscoverFiles() → LoadStrategy.Load()       │
│         → Profile.ExtractMetadata() → TranslationItem[]     │
│                                                             │
│  Save:  Profile.GetFilePath() → Profile.InjectMetadata()    │
│         → SaveStrategy.Save() → SourceControl.Stage()       │
│                                                             │
│  Validate: Pipeline.RunAll(items, settings, profile)        │
│         → Framework rules + Global rules → ValidationResult │
│                                                             │
└─────────────────────────────────────────────────────────────┘

TranslationItem stays as the universal atom — profiles wrap it with
framework-aware discovery, path generation, and metadata round-tripping.
```

---

## Migration Path (Non-Breaking)

1. `FrameworkTile` gains an `IFrameworkProfile` property (alongside existing `SaveStyles`)
2. `ProjectSettings` gains a `Framework` string field (persisted in `toucan.project`)
3. Existing `ILoadStrategy`/`ISaveStrategy` remain unchanged — profiles compose them
4. Old projects without `Framework` field fall back to `GenericJsonProfile` (current behavior)
5. New projects get proper framework detection

---

## Answer: Format vs Framework

**Format is just the serialization.** Framework adds:
- **Discovery** — where are the files and how to find them
- **Language extraction** — how to determine locale from path
- **Metadata** — descriptions, plural forms, max-length, translatable flags
- **Validation** — framework-specific correctness checks
- **Path generation** — where to write output files on save

The current code treats frameworks as a format selector. The fix is the `IFrameworkProfile` abstraction that sits *above* formats and composes them.
