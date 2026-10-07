---
title: "Writing Toucan plugins"
status: active
updated: 2026-10-07
summary: "Plugin author guide: manifest, formats, providers, rules, framework profiles, trust model, testing. Preview since v0.18.0; Avalonia app and CLI only."
---
# Writing Toucan plugins

A plugin is a .NET assembly in its own folder that adds things to Toucan: **file formats**, **machine-translation
providers**, **validation rules** and **framework profiles**. The working example is
[`samples/Toucan.Sample.Plugin`](../samples/Toucan.Sample.Plugin) (a tab-separated-values format plus a rule); its
build output is exercised by the test suite, so what it does is what this guide describes.

> Status: preview, since v0.18.0. Plugins run in the Avalonia app (macOS, Linux) and the `toucan` CLI; the Windows
> WPF app does not load plugins.

- [Quick start](#quick-start)
- [The manifest](#the-manifest-pluginjson)
- [What you can add](#what-you-can-add)
- [How plugins are loaded](#how-plugins-are-loaded)
- [Trust and signing](#trust-and-signing)
- [Versioning](#versioning)
- [Testing your plugin](#testing-your-plugin)
- [Troubleshooting](#troubleshooting)
- [Limits](#limits)
- [For maintainers](#for-maintainers)

## Quick start

1. Create a class library and reference the contract package. `ExcludeAssets="runtime"` matters: Toucan supplies the
   abstractions at run time, which is what makes your `ISaveStrategy` the same type as Toucan's.

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <EnableDynamicLoading>true</EnableDynamicLoading>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Toucan.Plugins.Abstractions" Version="1.0.0" ExcludeAssets="runtime" />
     </ItemGroup>
     <ItemGroup>
       <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
     </ItemGroup>
   </Project>
   ```

2. Add the entry point. Register everything in `Initialize`:

   ```csharp
   public sealed class MyPlugin : IToucanPlugin
   {
       public void Initialize(IPluginContext context)
       {
           var format = new MyFormat();          // implements ISaveStrategy and ILoadStrategy
           context.AddFormat(save: format, load: format);
           context.AddValidationRule(new MyRule());
       }
   }
   ```

3. Add `plugin.json` next to it ([reference](#the-manifest-pluginjson)):

   ```json
   {
     "id": "acme.my-format",
     "name": "Acme format",
     "version": "1.0.0",
     "apiVersion": "1.0",
     "entryAssembly": "Acme.MyFormat.dll",
     "capabilities": ["formats", "validation"]
   }
   ```

4. Build, then copy the output folder into the plugins folder as a subfolder:

   | | |
   |---|---|
   | Plugins folder | `Documents/Toucan/plugins/` (`TOUCAN_PLUGINS_DIR` overrides it) |
   | Result | `Documents/Toucan/plugins/acme.my-format/{plugin.json, Acme.MyFormat.dll, …}` |

5. Start Toucan (or run `toucan plugins list`). A new plugin is **not loaded until you trust it**: open
   Settings → Plugins and choose *Trust…*, or run `toucan plugins trust acme.my-format`, then restart.

## The manifest (`plugin.json`)

| Field | Required | Notes |
|---|---|---|
| `id` | yes | Unique and stable. Lowercase letters, digits, `.` and `-`; at most 64 characters; starts and ends with a letter or digit. Convention: `vendor.name`. |
| `name` | yes | Shown in Settings → Plugins. |
| `version` | yes | Your plugin's version, like `1.2.3`. |
| `apiVersion` | yes | The plugin API you built against, like `1.0` (see [Versioning](#versioning)). |
| `entryAssembly` | yes | File name of your assembly, inside the plugin folder. No paths, must end in `.dll`. |
| `entryType` | no | Full name of your `IToucanPlugin` class. Required if the assembly has more than one public implementation. |
| `capabilities` | no | Any of `formats`, `providers`, `validation`, `frameworks`. Registering something you did not declare fails the plugin. |
| `author`, `description` | no | Shown when the user decides whether to trust you. |

Comments and trailing commas are allowed. All problems are reported together, so one load attempt shows everything to fix.

## What you can add

Everything is registered through the `IPluginContext` passed to `Initialize`. It also gives you `Logger`,
`PluginDirectory` (for your own data files) and `HostApiVersion`. Your classes are created by you (`new`), not by a
container: pass what they need yourself.

### Formats: `context.AddFormat(save, load)` (capability `formats`)

Implement `ISaveStrategy` and `ILoadStrategy`; one class can do both. They must report the same `FormatId`.

| Member | Purpose |
|---|---|
| `FormatId` | Stable identifier, lowercase letters/digits/`.`/`_`/`-`. Saved in each project's `toucan.tproj` as `saveFormat`, so **never change it** once released. Must not collide with a built-in or another plugin. |
| `DisplayName` | Name in the export picker and `toucan list-formats`. |
| `FileExtensions` | Extensions (with dot) that map to this format when importing by file name. |
| `DefaultFilePath(language)` | Where a new project puts a language's file, relative to the project folder, with `/` separators. **Required.** |
| `LanguageFiles(root, language)` | Every file that holds a language's data (default: just the default path). Override if a language spans several files; used when removing a language. |
| `StoresCommentsInline` | `true` if your files can hold comments. Otherwise Toucan keeps comments in a `.comments.json` sidecar next to `CommentSidecarBase(language)`. |
| `Detection` | Optional `FormatDetection(priority, extensions, fileNames)` so folders of your files are recognised when there is no project file. Lower priority number wins; built-ins use 0–10. |
| `Save(path, context)` / `SaveAsync` | Write `context.LanguageDictionary` (language → items) into folder `path`. |
| `Load(folder)` | Return every `TranslationItem` (`Language`, `Namespace` = key, `Value`). |

Projects in your format work like any other: create, open, save, export, validate, translate. If the plugin is later
missing, those projects refuse to open with "format unavailable" instead of being misread as JSON, so users never
lose files because a plugin was disabled.

### Validation rules: `context.AddValidationRule(rule)` (capability `validation`)

Implement `IValidationRule`: `Id`, `Name`, `DefaultSeverity` and `Validate(ValidationContext)`, which yields
`ValidationResult`s. `ValidationContext` has `Items` (all translations) and `PrimaryLanguage`. Prefix rule IDs with
your plugin ID (`acme.no-todo`); IDs are global and cannot collide with built-ins. Rules run together with the built-in
ones (validate-on-save and `toucan check`) and are listed in Settings → Validation, where users can switch each one
off or change its severity. The name you return from `Name` is the label shown there.

### Providers: `context.AddProvider(provider)` (capability `providers`)

Implement `ITranslationProvider`: `Name` (unique, case-insensitive) and
`PretranslateAsync(jobs, options, progress, cancellationToken)`, returning one `PretranslationItemResult` per job
(`Succeeded`, `TranslatedValue` or `ErrorMessage`). Options and secrets arrive in `options.ProviderOptions`.
Set `Definition` (a `ProviderDefinition` with `Name`, `DisplayName`, `Description`, `OptionFields`, `SecretFields`,
`DefaultValues`) to appear in *Translation Providers…*; without it the provider works but is not listed. A
definition must have the same `Name` as the provider and must not set `IsBuiltIn`.

### Framework profiles: `context.AddFrameworkProfile(profile)` (capability `frameworks`)

Implement `IFrameworkProfile` (`Id`, `DisplayName`, `DefaultFormatId`, file patterns, `DiscoverFiles`,
`ExtractLanguage`, `GetFilePath`, `DetectionScore`). Profiles appear in the import and new-project dialogs.
`DefaultFormatId` should be a format you or Toucan provide.

## How plugins are loaded

- At startup Toucan looks at each subfolder of the plugins folder that contains a `plugin.json`, in name order.
- Each plugin gets its **own assembly load context**, so its private dependencies (a parser library, say) never
  clash with Toucan's or another plugin's. `Toucan.Plugins.Abstractions`, `Toucan.Core` and the
  `Microsoft.Extensions` abstractions always come from Toucan; copies in your folder are ignored.
- The manifest is checked, then the API version, then enabled/trust state. Only then is your assembly loaded.
- `Initialize` runs once. Registrations are collected and applied **only if it returns normally and every
  registration is valid**; a plugin that throws, or registers a duplicate ID, an undeclared capability or a malformed
  format contributes nothing, and does not stop other plugins or the app.
- Static constructors and `Initialize` run in-process with the user's permissions: keep startup fast and do not
  block. There is no timeout.
- Changes (adding, updating, enabling, trusting) take effect after a restart. Plugins are never unloaded.

## Trust and signing

A plugin is ordinary code, so Toucan loads it only if **you enabled it and trusted its exact files**:

- Trust is tied to a SHA-256 hash over every file in the plugin folder. Updating or editing any file means the user
  is asked again ("changed since you trusted it").
- Decisions live in `Documents/Toucan/plugin-policy.json`, shared by the app and the CLI.
- The CLI never prompts (it must work in CI): it loads enabled, trusted plugins, `toucan plugins trust <id>` records
  consent, and `--allow-plugin <id>` waives trust for a single run without saving anything.
- **Signing is not enforced yet.** Plugins show as "Not signed". It becomes mandatory when Toucan gains accounts and
  shared projects; design your release process so you can sign later (stable `id`, versioned releases).

## Versioning

`PluginApi.Current` is the plugin API version Toucan implements (currently **1.0**). The NuGet package version of
`Toucan.Plugins.Abstractions` tracks it. A plugin loads when its `apiVersion` has the **same major** version and a
**minor no newer** than the host's. So a plugin built for `1.0` runs on every `1.x`; one built for `1.2` is rejected
by a Toucan that implements `1.1` ("built for plugin API 1.2, but this Toucan implements 1.1"). Minor releases only
add members, with defaults wherever an existing implementation would otherwise break; a major release may break.

## Testing your plugin

- Point the CLI at a scratch folder and policy file so you never touch your real settings:

  ```bash
  export TOUCAN_PLUGINS_DIR=./plugins TOUCAN_PLUGIN_POLICY=./policy.json
  toucan plugins list
  toucan check ./my-project --allow-plugin acme.my-format
  toucan export ./my-project -f my-format -o ./out --allow-plugin acme.my-format
  ```

- `toucan plugins list` shows status, signature, hash, what the plugin registered and any error.
- Unit-test your strategies directly: they are plain classes (`Save` into a temp folder, `Load` it back, compare).
- The repository's own tests are a good template: `tests/Toucan.Core.Tests/Plugins/SamplePluginTests.cs` installs a
  built plugin into a temp folder and drives it through the real host.

## Troubleshooting

| Status / message | Meaning and fix |
|---|---|
| *NeedsTrust*: "you have not trusted it yet" | Expected for a new plugin. Trust it in Settings → Plugins or with `toucan plugins trust <id>`. |
| *NeedsTrust*: "its files have changed since you trusted it" | You rebuilt or edited it. Trust again. |
| *Disabled* | Switched off in Settings → Plugins (or `toucan plugins disable`). |
| *Rejected*: manifest errors | Fix the listed `plugin.json` problems. |
| *Rejected*: "Built for plugin API …" | Rebuild against an API version this Toucan implements. |
| *Rejected*: "Plugin ID … is already used" | Two folders declare the same `id`. |
| *Failed*: "Entry assembly … was not found" | `entryAssembly` does not match the file in the folder. |
| *Failed*: "has N IToucanPlugin implementations" | Set `entryType`. |
| *Failed*: "does not declare the 'x' capability" | Add it to `capabilities`. |
| *Failed*: "… is already provided by Toucan or another plugin" | Pick a unique format/provider/rule/profile ID. |
| *Failed*: exception text | Your constructor or `Initialize` threw; the message is the exception. |
| Project says "format … is not available" | The project uses a plugin format that is not installed, enabled and trusted. |
| Type or cast errors mentioning `ISaveStrategy` | A private copy of `Toucan.Plugins.Abstractions` is in your folder; remove it (`ExcludeAssets="runtime"`). |

## Limits

- Restart required for any change; no unloading.
- No dependency injection into plugin classes and no access to Toucan's internal services (use `System.IO`, your own
  HTTP client, and so on). Logging goes through `context.Logger`.
- No UI contributions yet (panels, dialogs, menu items). They will arrive as a separate package so headless hosts
  such as the CLI never load UI types.
- Per-project rule plugins (`.toucan/rules`) and a plugin feed are not implemented.
- The WPF app has no plugin support; plugins work in the Avalonia app and the CLI.

## For maintainers

- Build and test everything that is cross-platform with `dotnet test Toucan.CrossPlatform.slnx` (it leaves out the two
  Windows-only WPF projects, so it runs on macOS and Linux CI as well as Windows).
- The contract package: `dotnet pack Toucan.Plugins.Abstractions -c Release -o <dir>`. Bump `<Version>` only with
  `PluginApi.Current`, and never remove or change a public member within a major version.
- `Toucan.Plugins.Abstractions` must not reference `Toucan.Core` or UI packages; a test fails the build if it does.
- Formats, providers, rules and profiles that ship with Toucan are **built-in modules** (`Toucan.Modules.*`), not plugins:
  compiled in, always on, listed read-only by `toucan plugins list`, and their `toucan.*` IDs are reserved. They register
  through the same kinds of calls a plugin makes, so a plugin author can read them as examples. See
  [ARCHITECTURE.md](ARCHITECTURE.md) and [specs/plugin-modularization](specs/plugin-modularization/design.md).
