---
title: "plugin-modularization — requirements"
status: done
progress: "9/9 steps"
updated: 2026-10-07
summary: "Move the built-in formats, providers, validation rules and framework profiles out of Toucan.Core into compiled-in modules that register the same way external plugins do."
archived: 2026-10-07
reason: "Shipped in v0.20.0"
---
# Requirements: plugin modularization

## Introduction

Everything Toucan ships (14 save and 14 load strategies, 8 providers, 8 framework profiles, 6 validation rules, about 3,500 lines) lives in `Toucan.Core` and is wired by hand in `ToucanCoreServiceCollectionExtensions`. External plugins reach the system through `PluginHost`, built-ins through a different route. Core cannot be built, tested or trimmed without the formats, and Settings and the CLI treat the two kinds differently.

This work moves the built-ins into **built-in modules**: ordinary project references that register through the same shape as a plugin and show up in the same catalog. No behavior changes.

## Glossary

- **Built-in module**: a compiled-in assembly that adds formats, providers, rules or profiles through an `AddToucan<Name>Module` extension and describes itself in `IPluginCatalog`.
- **External plugin**: a folder with `plugin.json`, loaded by `PluginHost` behind the trust check. Unchanged by this work.
- **Common**: `Toucan.Core.Common`, the helpers every format module needs (`IFileService`, `FileService`, `FileEnumerator`, `NestedJsonParser`).

## Requirements

### R1: Core references no module

1. `Toucan.Core` SHALL NOT reference any `Toucan.Modules.*` assembly, directly or transitively.
2. `Toucan.Core` SHALL NOT contain a concrete built-in format, provider, rule or profile once migration is complete.
3. An architecture test SHALL fail the build if (1) or the existing rule "Abstractions does not reference Core" is broken.

### R2: Built-in modules register like plugins

1. Each module SHALL expose one `AddToucan<Name>Module(IServiceCollection)` extension.
2. Each module SHALL register a descriptor (id, name, version, capabilities, what it registered, source `BuiltIn`) into `IPluginCatalog`.
3. `toucan plugins list` and Settings → Plugins SHALL list built-in modules read-only, separately from external plugins.
4. External plugin IDs SHALL NOT be allowed to collide with built-in IDs (already enforced by `ReservedIds`; it SHALL keep working).
5. The plugin API version (`PluginApi.Current`) SHALL NOT change.

### R3: Behavior is preserved

1. The set, order and definitions of registered formats, providers, rules and profiles SHALL be identical before and after each step, proven by a snapshot test written before the first move.
2. Existing tests SHALL keep passing at every step; any test edit SHALL only change references, not expectations.
3. Implicit contracts SHALL become explicit before modules are split: the Manifest load strategy lookup, "Json before Manifest", "first provider is the fallback", and format detection priority.

### R4: Module granularity

1. Formats SHALL be grouped by family (Json, Xml, Text, Data), not one assembly per format.
2. Types that depend on each other concretely (Json, Namespaced, Manifest) SHALL live in the same module.
3. Providers, rules and profiles SHALL each be one module.

### R5: No static fallbacks

`BuiltInFormats`, `BuiltInProviders`, `FrameworkDetector`, the parameterless `TranslationProviderRegistry()` and the built-in path fallback in `ProjectSettings` SHALL be removed. Callers without a container SHALL be given the registrations explicitly.

### R6: Validation rules come from the registry

The Settings → Validation page SHALL list rules from the registered `IValidationRule`s, so rules from modules and plugins appear without a hard-coded list.

## Out of scope

- Shipping a module as an external plugin folder (possible later; needs no code change here).
- Per-format assemblies.
- Replacing the static `HttpClient` fields in providers.
- Plugin UI contributions.
