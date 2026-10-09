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
| `desktop` | no | The plugin's UI assembly: `{ "entryAssembly": "Acme.Ui.dll", "entryType": "Acme.Ui.Entry", "contractVersion": "1.0" }`. Needs the `desktop` capability. See [Desktop parts](#desktop-parts-ui). |
| `capabilities` | no | Any of `formats`, `providers`, `validation`, `frameworks`, `activation`, `commands`, `desktop`. Registering something you did not declare fails the plugin. |
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

### Activation: `context.AddActivator(id, activator)` (capability `activation`, API 1.1)

Registration happens once at startup and must not touch the network. Work with side effects (connecting, reading
remote state) belongs in an `IPluginActivator`, which Toucan runs per scope: `Lifetime` is `Application`,
`Workspace` (one open project) or `Connection` (one connection inside a project). Each activation gets its own
`IPluginActivationContext` (plugin, workspace and connection IDs, a logger, and a `Scope` token cancelled when the
scope ends) and returns an optional `IAsyncDisposable` session, disposed when the scope ends: newest first, and
connections before their workspace. An activator that throws fails only that activation; it is logged and raised
through `IPluginActivationService.ActivationFailed`, separately from load failures in the plugin catalog. Closing a
project cancels activations still in flight.

### Commands: `context.AddCommand(definition, handler)` (capability `commands`, API 1.1)

A command is something the user can run from the command palette, a menu or a shortcut. `CommandDefinition` gives it a
stable `Id` (it must start with your plugin ID and a dot), a `Title` and `Category` (with optional translations in
`LocalizedTitles` and `LocalizedCategories`), a `DefaultShortcut` such as `Mod+Shift+K` (`Mod` is Cmd on macOS and Ctrl
elsewhere), the `Placements` you want (palette, menu, toolbar, context menu) and whether it `RequiresWorkspace`.
`ICommandHandler.GetState` reports `Available`, `Hidden`, `Unavailable`, `Disconnected` or `Unlicensed` (keep it fast: it
runs whenever a menu is built); `ExecuteAsync` does the work, reports progress through `invocation.Progress` and must
honour its cancellation token, which is cancelled when the user cancels or the project closes. Users can reassign or clear
your shortcut in Settings → Shortcuts; the choice is kept by command ID. Commands that need a project are unavailable
until one opens and are cancelled when it closes.

### Host services: `context.Services` (API 1.1)

`IPluginServices` gives every plugin the same tidy set of things, scoped to its own ID. Use them from commands, activators
and views, after startup: during `Initialize` only register things (a service used then throws).

> **Plugins are not sandboxed.** They run in Toucan's process with your permissions, exactly like any program you install.
> These services keep a well-behaved plugin tidy and its secrets out of logs; they do not stop a malicious plugin from
> reading files, using the network or reading what other code in the process holds. That is why each plugin has to be
> trusted first, and why a changed plugin has to be trusted again.

| Service | What it does |
|---|---|
| `Storage` | A folder under `Documents/Toucan/plugin-data/<plugin id>` with atomic text and JSON writes. Paths are relative and cannot leave the folder. Never write next to your own DLLs: the plugin folder is trusted by its hash, so changing it makes Toucan ask again. |
| `Configuration` | Typed settings (below). |
| `Secrets` | Credentials in the encrypted secret store, scoped to the plugin and optionally a project or connection. Anything read or written is masked from then on. |
| `Notifier` | `Notify(new PluginNotification { Title, Message, Severity, ActionCommandId, ActionLabel, Sticky })`: a toast in the desktop app (clicking it runs the action's command), a line on stderr in the CLI, and an entry in the notification history. |
| `Operations` | `Start(new OperationOptions { Title }, async (ctx, ct) => …)`: runs in the background with progress (`ctx.Report(message, fraction)`) and a cancel button in the status bar. An exception ends it as failed and is reported, never thrown. Operations end when the project closes unless `CancelWithWorkspace` is false. |
| `Workspace` | Read and change the open project (below). |
| `Diagnostics` | `Redact(text)`, `Write(level, message)` for the report that **Help → Copy Diagnostics** builds, and `RegisterSecret(value)` for a token you received that did not come through `Secrets`. What `context.Logger` logs is masked too. |

**Settings.** Describe them once with `context.SetConfiguration(new ConfigSchema { Version = 1, Fields = [...] })`. A field has a
`Key`, `Label` (with `LocalizedLabels`), a `Type` (`Text`, `Boolean`, `WholeNumber`, `Number`, `Choice`, `Secret`, `Path`,
`Url`), a `Default`, `Required`, `Minimum`/`Maximum` (a number's bounds, or a text's length), a `Pattern`, `Choices`, and a
`Scope`: `App`, `Workspace` (one value per project) or `Connection` (one per connection, optionally within a project; pass
`ConfigTarget.ForConnection(id, workspaceId)`). Read with `Configuration.GetValue<T>(key, target)`; write with
`SetAsync`, which validates first and refuses a bad value without changing anything. A `Secret` field goes to the secret
store, never to the settings file, and `GetValue` never returns it (use `GetSecretAsync`). The desktop app generates a form
for the schema under your plugin on the Plugins page of Settings, so a simple plugin needs no settings UI of its own.

When a change would misread values saved by an older version, raise `Version` and add a `ConfigMigration(fromVersion, apply)`
that edits the old values in place (rename a key, convert a type). Toucan applies the migrations in order the first time it
loads older settings and writes the result back. If a step is missing, values that no longer fit fall back to their defaults
and the diagnostics say so. Mistakes in a schema (duplicate keys, a choice with no choices, a default that breaks its own
rule) fail the plugin at load with a message that names the field.

### Reading and changing the project: `Services.Workspace` (`IWorkspaceApi`)

A plugin never gets Toucan's models or view models. It reads an immutable **snapshot** and sends back an **edit**.

```csharp
var snapshot = await services.Workspace.SnapshotAsync(ct);          // null when no project is open
var edit = new WorkspaceEdit { Label = "Pulled 12 translations", BasedOnRevision = snapshot.Revision };
edit.SetValue("home.title", "fr", "Accueil", expectedValue: snapshot.Find("home.title", "fr")!.Value);
edit.SetReview("home.title", "fr", ReviewState.Approved);
var result = await services.Workspace.ApplyAsync(edit, ct);        // result.Applied, .ChangedUnits, .Issues, .Findings
```

- **Snapshot.** Keys, languages and every translation's text, comment, review state and whether it has unsaved changes. It
  includes what the user has typed but not yet committed, and it never changes afterwards. `Revision` goes up with every edit,
  yours or the user's.
- **Edit.** Steps run in order and later steps see earlier ones: `SetValue`, `SetComment`, `SetReview`, `AddKey`,
  `RenameKey` (a key and everything below it) and `DeleteKey`. The edit is checked in full before anything is touched, and by
  default it is **all or nothing**: one bad step refuses the whole edit and changes nothing. Set `AllowPartial` to apply the
  good steps and have the rest reported in `Issues`. Each issue has a kind (`UnknownKey`, `UnknownLanguage`, `InvalidKey`,
  `KeyExists`, `Conflict`, `Stale`, `ApprovalRefused`, `Empty`, `NoProject`) and the number of the step.
- **Not clobbering the user.** `BasedOnRevision` refuses the edit when anything changed since the snapshot. `expectedValue` on
  `SetValue` is narrower: only that translation must still have the text you saw.
- **Behaves like a native edit.** The values of an edit are **one undo step**; they mark the project as having unsaved changes,
  show in the open editor at once, are saved with the project, covered by autosave and offered back after a crash, and a failed
  save leaves them unsaved. Approving obeys the project's strict-approval policy, judged on the project as the edit leaves it.
  Adding, renaming and deleting keys are not undoable, exactly as in the editor.
- **Validation never blocks.** After applying, validation runs and the findings for what you changed come back in
  `result.Findings` (and appear in the Issues panel); they do not refuse or revert anything, just as they never stop a save.
- Calls may come from any thread; the host applies them on the UI thread. In a host with no open project (the CLI today) the
  workspace is simply never open.

### Desktop parts (UI)

The CLI has no UI, so a plugin's screens live in a second assembly that only the desktop app loads. Reference the
`Toucan.Plugins.Avalonia` package and Avalonia with `ExcludeAssets="runtime"` (Toucan supplies both; copying them next to
your plugin would make your controls different types from Toucan's), name the assembly in `plugin.json` under `desktop`,
and implement `IToucanDesktopPlugin`:

```csharp
public sealed class Entry : IToucanDesktopPlugin
{
    public void InitializeDesktop(IDesktopPluginContext context)
    {
        context.AddSidePanel(new SidePanelContribution
        {
            Id = "acme.sync.panel", Title = "Sync", Slot = PanelSlot.Right, Icon = "Sync",
            Actions = [new PanelAction("acme.sync.pull", "Download", "Pull changes")],
            CreateContent = workspace => new SyncPanel(workspace),
        });
    }
}
```

What you can add (all IDs start with your plugin ID and a dot; nothing is registered if the entry point throws):

| Contribution | Where it appears |
|---|---|
| `AddSidePanel` | An activity-bar button and a panel in the left or right side bar, with a toolbar of command buttons. |
| `AddInspectorSection` | A section at the bottom of the inspector while a key is selected. |
| `AddSettingsPage` | A group under the plugin list on the Plugins page of Settings. |
| `AddDialog` | A window you open with `context.Host.ShowDialogAsync(id)`. |
| `AddStatusBarItem` | An item at the left or right end of the status bar (text, icon, tooltip, badge with a severity, optional click command). It returns an `IStatusBarItem` you keep and change from any thread; the display updates on the UI thread. An item with nothing to show takes no room. |
| `AddEditorAction` | A button in the inspector's key actions and an item in the key context menus; it runs a command with the key's name as parameter. |

The desktop context also carries `Services`, the same `IPluginServices` the main part gets. Views get an `IPluginWorkspace`, not Toucan's view models: whether a project is open, its folder, the selected key, a
`Changed` event and `ExecuteCommandAsync`. Conventions:

- **Theme.** Use `DynamicResource` with the keys in `DesktopTheme` (`TextBrush`, `MutedTextBrush`, `CardBackgroundBrush`, `BadBrush`, …) so views follow light and dark mode. Other host resource keys may change.
- **Icons.** Name an icon from the host's icon set (`Sync`, `Add`, `Setting`, …); an unknown name shows no icon, so always give a tooltip.
- **Localization.** Titles take a `LocalizedTitles` table by culture (`fr`, `pt-BR`); the host falls back to the parent culture, then the default. Text inside your own views is yours to localize; `context.Host.Culture` is the UI culture.
- **Accessibility.** Give controls without visible text an `AutomationProperties.Name`, keep everything reachable with the keyboard, and do not rely on colour alone. Toolbar buttons and key actions already carry their tooltip as their accessible name.
- **Contract version.** `desktop.contractVersion` follows the same rule as the plugin API: the same major version and a minor no newer than the host's (currently `DesktopContract.Current`, 1.0). A newer one is reported on the Plugins page and the desktop part is skipped; the rest of the plugin still works.
- **Shared assemblies.** Avalonia, FluentAvalonia and `Toucan.Plugins.Avalonia` always come from Toucan; a copy in your folder is ignored.

If the desktop part cannot load, the plugin still loads without its UI and the Plugins page says why.

## Versioning

`PluginApi.Current` is the plugin API version Toucan implements (currently **1.1**). The NuGet package version of
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
