# Toucan.Core — Documentation

## Overview
Toucan.Core is the cross-platform core logic for the Toucan application. It contains models, extension methods, parsers and lightweight services responsible for reading/writing translation files and generating UI-friendly structures, like namespace trees and statistics.

Key responsibilities:
- Parse JSON files into in-memory `TranslationItem` objects
- Generate and persist different save formats (namespaced JSON, flattened JSON)
- Build a namespaced tree representation (`NsTreeItem`) for UI
- Provide extension utilities (`TranslationItemExtensions`) for filtering and grouping
- Small services for paging and language-related validation

## Key Interfaces
- `IParser` — Contract to parse file content into `TranslationItem` objects. Implemented by `JsonParser`.
- `IProjectFile` — Loader and language creation helper for reading a translation project.
- `ITranslationFile` — Contract for saving a list of `ITranslationItem` objects.
- `ITranslationItem` — Basic shape of a translation unit (Namespace, Value, Language)
- `IFileService` — Small read/save/delete helper for generic JSON persistence.
- `IRecentProjectService` — Manage recent projects list (used by UI layer).

## Models
- `TranslationItem` — The core unit (Language, Namespace, Value)
- `NsTreeItem` — Tree nodes used by UI to visualize nested namespaces
- `Project` — Represents a translation project with `Path`, `Name`, `LastOpened` and `IsValid()`.
- `SummaryItem` / `SummaryInfo` — A view model structure representing translation completeness information.
- `SaveStyles` — Enum to choose save formats (Json, Namespaced, Properties, Yaml, Adb)

## Services
- `FileService` — Provides file-based persistence. Small, synchronous API: `Read<T>`, `Save<T>`, `ReadText`, `SaveText`, `ReadBytes`, `SaveBytes` and `Delete`.
  * For `T` types that are not `string` or `byte[]`, `Read<T>`/`Save<T>` use `Newtonsoft.Json` for serialization/deserialization. For raw text and binary files, use the `ReadText`/`SaveText` or `ReadBytes`/`SaveBytes` methods.
- `ProjectHelper` (in code this is a static helper) — Reads JSON files in a folder and produces a `List<TranslationItem>`. Also provides `SaveJson` and `SaveNsJson`.
- `JsonParser` — Implementation of `IParser` which walks JSON tokens and produces `TranslationItem` objects. It supports nested JSON and returns a queue-based BFS/DFS enumeration of leaf nodes.
- `LanguageService` — Singleton-like internal validation for language uniqueness.
- `PagingController<T>` — Generic paging controller for UI lists.

## Flow / Behavior (high-level)
- On opening a project, the app uses `ProjectHelper.Load(folder)` to gather all language files `*.json` in `folder`.
- `ProjectHelper` loads each file, determines language from filename, uses `FromNestMethod` to parse nested JSON into `TranslationItem` objects.
- `TranslationItem` objects are aggregated and moved through `TranslationItemExtensions` where states, languages and namespaces can be derived for UI and other operations.
- On save, `SaveJson` (flat output) or `SaveNsJson` (namespaced output) output per-language files using `TranslationItem` groups.
- `JsonParser` also implements `IParser`, allowing a streaming/async-style parse from a JSON content string.

## Strengths
- Separation of responsibilities — models and extension methods are nicely contained and reusable.
- `IParser` abstraction allows future file types (XML, PO, RESX) by supplying new implementations.
- Namespacing utilities (`ToNsTree`, `ToLanguageDictionary`) provide convenient transforms for UI representation.

## Observations / Current limitations
- A number of service methods are `static`, e.g., `ProjectHelper`. These are less testable and hard to mock for unit tests.
- There's inconsistent use of asynchronous vs synchronous APIs — e.g., file IO is synchronous, but `IParser.Parse` returns `IAsyncEnumerable` (good candidate for streaming). Unifying/clarifying the IO model will help.
- `FileService` and `ProjectHelper` use `Newtonsoft.Json` synchronously — consider streaming for large files.
- `ProjectHelper` and `JsonParser` use `Console.WriteLine` for error reporting — better to use proper logging.
- `PagingController` and `ProjectHelper` code snippets contain unusual patterns (looks like array literal `[]` shorthand or `new()` shorthand used inconsistently) — appears to break standard C# conventions / or is truncated. Suggest checking and formatting/compilation.
- The project uses raw `Path.Combine` and string concatenation in places — unify and enforce cross-platform path handling.

## Tests & CI
- There are no tests in `Toucan.Core` in this snapshot. Add unit tests for:
  - `JsonParser` parse correctness (simple JSON, edge cases: empty object, nested arrays, null values).
  - `ProjectHelper.Load` and Save methods.
  - `TranslationItemExtensions` transforms.
  - `PagingController`: paging math and behavior.

## Short tactical improvements (high priority)
- ✅ Convert `ProjectHelper` static methods to an instance class + `IProjectService` (interface) so we can mock and unit test the file-level logic.
- ✅ Replace synchronous file IO where appropriate with async methods — especially Save & Load. Use `Task`-based IO (e.g., `File.ReadAllTextAsync`) and create `IFileService` async methods or wrap them.
- ✅ Add structured logging (e.g., Microsoft.Extensions.Logging) and remove `Console.WriteLine` usage.
- ✅ Add tests for parsers, extension methods, file IO and paging.
- ✅ Fix syntax anomalies and ensure all code compiles cleanly (the code contains `[]` and other shorthand we need to review).

## Feature / Design improvements (medium-term)
- Implement `ParserFactory` or `IParserFactory` to choose parsers based on file extension (JSON, RESX, PO, YAML). This supports future formats easily.
- Add save-style strategy pattern: `ISaveStrategy` with implementations for Json, Namespaced, Properties, YAML — selected by `SaveStyles` enum.
- Add streaming JSON parse (JsonTextReader) for very large files to reduce memory usage and improve performance.
- Provide a robust path and filename normalizer to avoid language name mismatches.
- Improve `LanguageService` to read from actual folder contents; provide Add/Remove languages with validation.
- Implement an `INotificationService` for recoverable parsing errors (per file) that the UI can display.

## Long-term / Nice-to-have
- Add code generation for language constant classes (so devs can reference keys in code safely)
- Add import/export capabilities for other localization tools (XLIFF, PO files)
- Add a change-tracking layer to generate diffs between languages and produce actions (e.g., 'Copy missing translations from base language')

## Example usage snippet
- Parsing a JSON translation file (existing code):

```csharp
IParser parser = new JsonParser("en");
await foreach (var item in parser.Parse(jsonContent))
{
    // use translation item
}
```

- Loading project via helper (current global helper):

```csharp
List<TranslationItem> items = ProjectHelper.Load(folderPath);
```

- Saving namespaced output per language

```csharp
ProjectHelper.SaveNsJson(folderPath, nsTreeItems, languagesList);
```

## Roadmap of Improvements (scoped steps)
1. Add unit tests for `TranslationItemExtensions` and `JsonParser`. (small)
2. Convert `ProjectHelper` to `IProjectService` and implement it using `IFileService` to decouple IO and logic. (medium)
3. Add `ILogger` across services and remove console writes. (small)
4. Implement `IParserFactory` and add at least a mock parser for other formats; ensure `JsonParser` is used by default. (medium)
5. Refactor `PagingController` to use standard collections and correct math; add tests. (small)
6. Add save strategies for `SaveStyles`. (medium)
7. Add integration tests that write temporary files and validate `SaveJson` / `SaveNsJson` output. (medium)
