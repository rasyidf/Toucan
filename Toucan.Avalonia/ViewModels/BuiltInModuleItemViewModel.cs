using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One read-only row of Settings → Plugins → Built-in modules: what ships with Toucan, its version and what it registered.</summary>
public sealed class BuiltInModuleItemViewModel(BuiltInModuleInfo module)
{
    public string Id => module.Id;
    public string Name => module.Name;
    public string Version => module.Version;
    public string Description => module.Description ?? string.Empty;
    public bool HasDescription => Description.Length > 0;
    public bool HasVersion => Version.Length > 0;
    public string ProvidesText => string.Join(", ", module.Registered);
}
