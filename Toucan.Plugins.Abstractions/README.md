# Toucan.Plugins.Abstractions

The contract between [Toucan](https://github.com/rasyidf/Toucan) and its plugins. Reference this package to write a
plugin that adds file formats, machine-translation providers, validation rules or framework profiles.

```xml
<PackageReference Include="Toucan.Plugins.Abstractions" Version="1.0.0" ExcludeAssets="runtime" />
```

```csharp
public sealed class MyPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new MyFormat();                 // implements ISaveStrategy and ILoadStrategy
        context.AddFormat(format, format);
        context.AddValidationRule(new MyRule());
    }
}
```

Your plugin ships as a folder with a `plugin.json` manifest and your assembly. Toucan supplies this package at run
time, so do not copy it into your plugin folder (`ExcludeAssets="runtime"` on the package reference,
or `<Private>false</Private>` on a project reference), and set `<EnableDynamicLoading>true</EnableDynamicLoading>`.

The package version is the plugin API contract: a plugin built against API `1.x` loads in any Toucan that
implements API `1.y` with `y >= x`. See `docs/plugins.md` in the Toucan repository for the full authoring guide.
