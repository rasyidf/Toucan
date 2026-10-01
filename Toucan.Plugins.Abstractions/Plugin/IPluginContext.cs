using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;

namespace Toucan.Plugins;

/// <summary>What a plugin can contribute, and the host information it can read.</summary>
public interface IPluginContext
{
    /// <summary>The plugin API version the host implements.</summary>
    Version HostApiVersion { get; }

    /// <summary>Folder the plugin was loaded from (for its own data files).</summary>
    string PluginDirectory { get; }

    /// <summary>Logger scoped to this plugin.</summary>
    ILogger Logger { get; }

    /// <summary>
    /// Adds a translation file format. <paramref name="save"/> and <paramref name="load"/> must share the same
    /// <c>FormatId</c>; the ID must not collide with a built-in or another plugin's format.
    /// </summary>
    void AddFormat(ISaveStrategy save, ILoadStrategy load);

    /// <summary>Adds a machine-translation provider. Its <c>Definition</c> is required for it to appear in provider settings.</summary>
    void AddProvider(ITranslationProvider provider);

    /// <summary>Adds a validation rule. Rule IDs should be prefixed with the plugin ID (e.g. <c>acme.max-length</c>).</summary>
    void AddValidationRule(IValidationRule rule);

    /// <summary>Adds a framework profile shown in the import and new-project dialogs.</summary>
    void AddFrameworkProfile(IFrameworkProfile profile);
}
