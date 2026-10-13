using Toucan.Plugins;

namespace Toucan.Core.Plugins;

public enum PluginStatus
{
    /// <summary>Loaded and its registrations are active.</summary>
    Loaded,

    /// <summary>Found and enabled but not loaded: the user has not trusted this exact content yet.</summary>
    NeedsTrust,

    /// <summary>Found but switched off by the user; nothing was loaded.</summary>
    Disabled,

    /// <summary>Not loaded because of its manifest or compatibility (bad manifest, API version, duplicate ID).</summary>
    Rejected,

    /// <summary>Loading or initialization threw, or its registrations were invalid; nothing it registered is active.</summary>
    Failed,
}

/// <summary>Outcome of attempting to load one plugin folder. <see cref="Manifest"/> is null when it could not be read.</summary>
public sealed record PluginLoadResult(
    string Directory,
    PluginStatus Status,
    PluginManifest? Manifest = null,
    string? Error = null,
    IReadOnlyList<string>? Registered = null,
    string? ContentHash = null,
    PluginSignatureStatus Signature = PluginSignatureStatus.NotSigned,
    PluginTrustState? Trust = null,
    System.Runtime.Loader.AssemblyLoadContext? LoadContext = null)
{
    public string DisplayId => Manifest?.Id is { Length: > 0 } id ? id : Path.GetFileName(Directory);
}

/// <summary>What the host loaded at startup, for the plugin management UI and the CLI.</summary>
public interface IPluginCatalog
{
    IReadOnlyList<PluginLoadResult> Plugins { get; }

    /// <summary>Modules compiled into Toucan. Read-only: they have no trust or enable state.</summary>
    IReadOnlyList<BuiltInModuleInfo> BuiltInModules => [];
}

internal sealed class PluginCatalog(IReadOnlyList<PluginLoadResult> plugins, IReadOnlyList<BuiltInModuleInfo>? builtInModules = null) : IPluginCatalog
{
    public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
    public IReadOnlyList<BuiltInModuleInfo> BuiltInModules { get; } = builtInModules ?? [];
}
