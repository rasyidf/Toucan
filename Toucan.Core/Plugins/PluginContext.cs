using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>A plugin's registration attempt was invalid (undeclared capability, ID collision, malformed metadata).</summary>
public sealed class PluginRegistrationException : Exception
{
    public PluginRegistrationException(string message) : base(message) { }
    public PluginRegistrationException() { }
    public PluginRegistrationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A command a plugin registered, held until the command registry is built.</summary>
public sealed record PluginCommandRegistration(string PluginId, CommandDefinition Definition, ICommandHandler Handler);

/// <summary>IDs already taken by built-ins and plugins loaded earlier. Mutated only when a plugin is accepted.</summary>
internal sealed class ReservedIds
{
    public HashSet<string> Formats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Collects one plugin's registrations without touching the host. The host applies them only if
/// <see cref="IToucanPlugin.Initialize"/> returns and every registration is valid, so a failing plugin
/// contributes nothing.
/// </summary>
internal sealed partial class PluginContext : IPluginContext
{
    private readonly PluginManifest _manifest;
    private readonly ReservedIds _reserved;

    private readonly HashSet<string> _formats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activators = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _commandIds = new(StringComparer.Ordinal);

    public PluginContext(PluginManifest manifest, string pluginDirectory, ILogger logger, ReservedIds reserved, PluginServices? services = null, IDiagnosticsService? diagnostics = null)
    {
        _manifest = manifest;
        _reserved = reserved;
        PluginDirectory = pluginDirectory;
        // What a plugin logs is masked before it reaches any log, and is kept for the diagnostics report.
        Logger = diagnostics is null ? logger : new RedactingLogger(logger, diagnostics, manifest.Id);
        _services = services;
    }

    private readonly PluginServices? _services;
    private bool _configurationSet;

    public IPluginServices Services => _services ?? throw new InvalidOperationException("Plugin services are not available in this host.");

    public void SetConfiguration(ConfigSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (_services is null) throw new InvalidOperationException("Plugin services are not available in this host.");
        if (_configurationSet) throw new PluginRegistrationException($"Plugin '{_manifest.Id}' set its configuration twice.");
        _services.PluginConfiguration.SetSchema(schema);
        _configurationSet = true;
    }

    internal PluginServices? ServicesInternal => _services;

    public Version HostApiVersion => PluginApi.Current;
    public string PluginId => _manifest.Id;
    public string PluginDirectory { get; }
    public ILogger Logger { get; }

    public List<(ISaveStrategy Save, ILoadStrategy Load)> Formats { get; } = [];
    public List<ITranslationProvider> Providers { get; } = [];
    public List<IValidationRule> Rules { get; } = [];
    public List<IFrameworkProfile> Profiles { get; } = [];
    public List<RegisteredActivator> Activators { get; } = [];
    public List<(CommandDefinition Definition, ICommandHandler Handler)> Commands { get; } = [];

    /// <summary>Human-readable list of what was registered, e.g. <c>format:acme-po</c>.</summary>
    public IReadOnlyList<string> Summary =>
    [
        .. Formats.Select(f => $"format:{f.Save.FormatId}"),
        .. Providers.Select(p => $"provider:{p.Name}"),
        .. Rules.Select(r => $"rule:{r.Id}"),
        .. Profiles.Select(p => $"framework:{p.Id}"),
        .. Activators.Select(a => $"activator:{a.Id}"),
        .. Commands.Select(c => $"command:{c.Definition.Id}"),
    ];

    public void AddFormat(ISaveStrategy save, ILoadStrategy load)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(load);
        RequireCapability(PluginCapabilities.Formats);

        var id = save.FormatId;
        if (!IsValidFormatId(id))
            throw new PluginRegistrationException($"Format ID '{id}' is invalid: use lowercase letters, digits, '.', '_' and '-'.");
        if (!FormatIdsEqual(id, load.FormatId))
            throw new PluginRegistrationException($"Save strategy format '{id}' and load strategy format '{load.FormatId}' differ.");
        Claim(_reserved.Formats, _formats, id, "Format");

        Formats.Add((save, load));
    }

    public void AddProvider(ITranslationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        RequireCapability(PluginCapabilities.Providers);

        var name = provider.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new PluginRegistrationException("A provider needs a non-empty Name.");
        if (provider.Definition is { } definition)
        {
            if (definition.IsBuiltIn)
                throw new PluginRegistrationException($"Provider '{name}' cannot mark its definition as built-in.");
            if (!string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new PluginRegistrationException($"Provider '{name}' has a definition named '{definition.Name}'; the names must match.");
        }
        Claim(_reserved.Providers, _providers, name, "Provider");

        Providers.Add(provider);
    }

    public void AddValidationRule(IValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        RequireCapability(PluginCapabilities.Validation);

        if (string.IsNullOrWhiteSpace(rule.Id))
            throw new PluginRegistrationException("A validation rule needs a non-empty Id.");
        Claim(_reserved.Rules, _rules, rule.Id, "Validation rule");

        Rules.Add(rule);
    }

    public void AddFrameworkProfile(IFrameworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RequireCapability(PluginCapabilities.Frameworks);

        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new PluginRegistrationException("A framework profile needs a non-empty Id.");
        Claim(_reserved.Profiles, _profiles, profile.Id, "Framework profile");

        Profiles.Add(profile);
    }

    public void AddActivator(string id, IPluginActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        RequireCapability(PluginCapabilities.Activation);

        if (string.IsNullOrWhiteSpace(id))
            throw new PluginRegistrationException("An activator needs a non-empty id.");
        if (!_activators.Add(id))
            throw new PluginRegistrationException($"Activator '{id}' is registered twice by this plugin.");

        Activators.Add(new RegisteredActivator(_manifest.Id, id, activator));
    }

    public void AddCommand(CommandDefinition definition, ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        RequireCapability(PluginCapabilities.Commands);

        // The registry validates ID shape, prefix and shortcut when the host applies the plugin; here only own duplicates.
        if (definition.Id is null || !definition.Id.StartsWith(_manifest.Id + ".", StringComparison.Ordinal))
            throw new PluginRegistrationException($"Command '{definition.Id}' must start with its plugin ID '{_manifest.Id}.'.");
        if (!_commandIds.Add(definition.Id))
            throw new PluginRegistrationException($"Command '{definition.Id}' is registered twice by this plugin.");

        Commands.Add((definition, handler));
    }

    /// <summary>Marks this plugin's IDs as taken so later plugins cannot reuse them.</summary>
    public void Commit()
    {
        _reserved.Formats.UnionWith(_formats);
        _reserved.Providers.UnionWith(_providers);
        _reserved.Rules.UnionWith(_rules);
        _reserved.Profiles.UnionWith(_profiles);
    }

    private void RequireCapability(string capability)
    {
        if (!_manifest.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            throw new PluginRegistrationException(
                $"Plugin '{_manifest.Id}' registered something that needs the '{capability}' capability, which its manifest does not declare.");
    }

    private static void Claim(HashSet<string> reserved, HashSet<string> own, string id, string kind)
    {
        if (reserved.Contains(id))
            throw new PluginRegistrationException($"{kind} '{id}' is already provided by Toucan or another plugin.");
        if (!own.Add(id))
            throw new PluginRegistrationException($"{kind} '{id}' is registered twice by this plugin.");
    }

    private static bool FormatIdsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidFormatId(string? id) => !string.IsNullOrEmpty(id) && FormatIdPattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9]([a-z0-9._-]*[a-z0-9])?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatIdPattern();
}
