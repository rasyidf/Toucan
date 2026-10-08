namespace Toucan.Core.Plugins;

public sealed class PluginHostOptions
{
    /// <summary>Folders whose immediate subfolders are plugins (each with a plugin.json). Missing folders are skipped.</summary>
    public IList<string> Roots { get; } = [];

    /// <summary>Returns false to skip a plugin by ID without loading it. Null enables everything.</summary>
    public Func<string, bool>? IsEnabled { get; set; }

    /// <summary>
    /// The user's enable/trust decisions. When set, a plugin loads only if it is enabled and its exact content is
    /// trusted; anything else is reported as <see cref="PluginStatus.Disabled"/> or <see cref="PluginStatus.NeedsTrust"/>
    /// and none of its code runs. Null skips the check (tests, embedding).
    /// </summary>
    public IPluginPolicy? Policy { get; set; }

    /// <summary>Plugin IDs whose trust is waived for this run only (CLI <c>--allow-plugin</c>); enabled-state still applies.</summary>
    public ISet<string> AllowForThisRun { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Publisher signature check. Defaults to the stub that reports everything unsigned.</summary>
    public IPluginSignatureVerifier SignatureVerifier { get; set; } = new UnsignedPluginSignatureVerifier();

    /// <summary>
    /// Extra assembly names that plugins must share with the host instead of loading their own copy.
    /// <c>Toucan.Plugins.Abstractions</c>, <c>Toucan.Core</c> and the Microsoft.Extensions abstractions are always shared.
    /// </summary>
    public ISet<string> SharedAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Assembly name prefixes that always come from the host (the UI framework, for hosts that load desktop parts), so a
    /// plugin that ships its own copy by mistake cannot break type identity.
    /// </summary>
    public ISet<string> SharedAssemblyPrefixes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-user plugin folder: <c>Documents/Toucan/plugins</c>, next to the app's other settings.</summary>
    public static string DefaultRoot() => Path.Combine(
        Toucan.Core.Services.UserDataFolder.Root, "Toucan", "plugins");
}
