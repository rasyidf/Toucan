---
title: Toucan.Core interop architecture
status: active
updated: 2026-10-25
summary: Core workspace state, identity, history, async commands/subscriptions, document loading, format contracts, and save integration. Includes compatibility projections and source migration work.
---

# Toucan.Core interop architecture

Toucan normalizes translation resources into a shared model and resolves format implementations through DI. Core coordinates project IO and shared services; concrete parsers and writers live in built-in modules or external plugins. See the [application architecture](../docs/ARCHITECTURE.md) for host composition, desktop state, lifecycle, AI, and trust integration.

## Model and boundaries

[WorkspaceService](Services/WorkspaceService.cs) owns immutable committed units addressed by entry ID and language, workspace revisions, dirty baselines, approval policy, and transaction history. It exposes legacy and identified plugin APIs without UI dependencies. Rename/delete, comments, approvals, and values share undo/redo. Session GUIDs change on open and Save As, while the plugin workspace/configuration identifier remains the project path. Opt-in document save planning separates source reads, framework destinations, and document serialization; it captures only affected file targets and source versions. The opt-in document coordinator executes version-checked atomic writes with declared-target recovery and acknowledges captured state while preserving newer edits. Affected metadata and identity catalogs participate in the same transaction. Explicit document-capable framework projects use this pipeline in the desktop, including create-only Save As/export. Content-version watcher acknowledgment covers only committed targets; unrelated external edits remain visible. Core navigation projections and clean/dirty source refresh are implemented; navigator UI and connection lifecycle work remain open. Core async commands use a bounded serialized queue. Optional API 1.2 subscriptions atomically capture a snapshot and stream revisioned changes through independent bounded buffers; overflow requires resubscribing. Closing ends the session streams and cancels queued work. The desktop dispatches transactions on the UI thread through the same Core implementation.

[TranslationItem](../Toucan.Plugins.Abstractions/Models/TranslationItem.cs) remains a mutable compatibility projection used by the legacy editor store and serializers, which address records by `(Language, Namespace)`. Native editor changes commit a projection transaction into Core; plugin commands plan/apply there directly. Authoritative snapshots retain no mutable projection objects. Legacy dotted-key aliases that identify multiple entries are refused for edits; identified APIs use the entry GUID instead.

[WorkspaceSourceLoader](Services/WorkspaceSourceLoader.cs) supports opt-in document-aware JSON loading with structured paths, source versions, explicit framework resource families, and identity checkpoints. Explicit document-capable framework projects use the source loader and targeted persistence; other projects retain legacy loading/serialization. Settings and individual format implementations govern escaping, plurals, and round-trip behavior; normalization does not guarantee lossless conversion between formats.

Public plugin contracts and DTOs reside in `Toucan.Plugins.Abstractions`, even when their namespaces begin with `Toucan.Core`. Shared parsing/file helpers and the built-in module registration seam reside in `Toucan.Core.Common`. Core references these projects but no built-in module. Modules reference Common and Abstractions without referencing Core or sibling modules. [ArchitectureTests](../tests/Toucan.Core.Tests/Modules/ArchitectureTests.cs) enforces these rules.

```mermaid
flowchart LR
    Host["Desktop or CLI"] --> Core["Core project services and registries"]
    Host --> Defaults["Defaults composition"]
    Defaults --> Modules["Built-in format modules"]
    Modules --> Common["Core.Common"]
    Core --> Common
    Core --> Contracts["Plugins.Abstractions"]
    Common --> Contracts
    Plugin["External format plugin"] --> Contracts
```

## Format contract

[ILoadStrategy](../Toucan.Plugins.Abstractions/Contracts/ILoadStrategy.cs) exposes `FormatId` and `Load(folder)`. [ISaveStrategy](../Toucan.Plugins.Abstractions/Contracts/ISaveStrategy.cs) writes a `SaveContext` and owns file-layout metadata: `DefaultFilePath`, `LanguageFiles`, `FileExtensions`, `CommentSidecarBase`, `StoresCommentsInline`, and optional `Detection`.

`TranslationStrategyFactory` resolves these registrations by string ID. `FormatDetector` evaluates save-strategy detection rules. A plugin format participates through the same contracts; the orchestration layer does not need a new format-specific branch for ordinary loading and serialization.

| Family module | Registered resource formats |
|---|---|
| `Toucan.Modules.Formats.Json` | JSON, namespaced JSON, ARB; also the manifest loader |
| `Toucan.Modules.Formats.Xml` | Android XML, XLIFF, RESX |
| `Toucan.Modules.Formats.Text` | PO, INI, Java properties, iOS strings, Laravel PHP, CSV |
| `Toucan.Modules.Formats.Data` | YAML, TOML |

INI is save-only. Format availability, layout conventions, and fidelity must be checked against its registered strategies rather than inferred from a general supported-formats list.

## Project loading

The persisted manifest is **`toucan.tproj`**, loaded by [ProjectSettings](Models/ProjectSettings.cs). It stores a string `saveFormat`, languages, package URLs, and project preferences. Legacy `saveStyle` values migrate on load.

```mermaid
flowchart TD
    Folder["Project folder"] --> Settings{"toucan.tproj exists?"}
    Settings -->|Yes| Manifest["Load ProjectSettings and configured format"]
    Settings -->|No| Detect["Create defaults and detect format"]
    Manifest --> Resolve["Resolve load strategy by format ID"]
    Detect --> Resolve
    Resolve --> Missing{"Unknown format without loader?"}
    Missing -->|Yes| Fail["FormatUnavailableException"]
    Missing -->|No| Mode{"Manifest mode?"}
    Mode -->|Yes| Packages["Try ManifestLoadStrategy package paths"]
    Packages -->|No items| Load["Use selected loader"]
    Mode -->|No| Load
    Packages -->|Items| Alias["Apply language aliases"]
    Load --> Alias
    Alias --> Result["ProjectLoadResult: settings and translations"]
```

[ProjectService](Services/ProjectService.cs) performs this integration. [ProjectModeResolver](Services/ProjectModeResolver.cs) checks for the manifest; framework detection is a separate registered capability, not a call made by this resolver. Built-in formats without a loader retain the legacy JSON fallback. Unknown external format IDs fail explicitly to prevent misreading and subsequent overwriting.

The desktop lifecycle wraps loading with cancellation/progress, unsaved-change handling, baseline initialization, file watching, metadata/comments, and auto-save. Direct `ProjectService` callers do not automatically receive those steps.

## Save and metadata integration

`ProjectService.Save` builds a `SaveContext` containing language groups, language IDs, and namespace tree items, then calls the selected writer. Language aliases are reversed for output and restored for in-memory display. The project-aware overload updates manifest languages and applies configured encoding/line endings to known built-in language files; Java properties keeps Latin-1 encoding.

The desktop [ProjectLifecycleService](Services/ProjectLifecycleService.cs) validates before saving. Findings are reported in the Issues panel and do not block draft saves; only strict policies (such as `requireValidForApproval`) or explicit enforcement stop a save. Saves write to temporary files and swap them atomically, backed by a journaled rollback transaction (`SaveTransaction`) that restores previous files if an error occurs. When exact-resource strategies are configured, document presentation and file-level metadata are preserved.

[CommentPersistenceService](Services/CommentPersistenceService.cs) delegates inline-comment support and sidecar placement to strategy metadata. [AuditService](Services/AuditService.cs) persists `.toucan-metadata.json`. Neither arbitrary format conversion nor a direct writer call guarantees preservation of every source format's metadata.

## Adding a format

1. Implement the load and/or save contracts in the appropriate family module. Use a stable format ID; a read/write pair shares it.
2. Declare file layout, extensions, detection, and comment capabilities on the writer. Check multiple-language and nested-path behavior.
3. Register using `AddFormatStrategy` in that module. Keep Core independent of concrete implementations.
4. Update module snapshots and add meaningful load/save round-trip tests, including escaping, plural data, metadata, and encoding where relevant.
5. For external distribution, use the [plugin guide](../docs/plugins.md) and [sample plugin](../samples/Toucan.Sample.Plugin). Do not add a module reference to Core.

Cross-tool manifest import, including new ecosystems, requires an explicit implementation and tests; similar resource syntax alone is not evidence of complete interoperability. Future editor syntax coloring is separate from format parsing and remains in the [highlighting plan](../docs/specs/design/editor-syntax-highlighting.md).
