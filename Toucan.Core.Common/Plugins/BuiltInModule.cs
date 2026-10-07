using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// A module compiled into Toucan (as opposed to an external plugin folder). It registers the same kinds of
/// things a plugin does, through the service collection, and is listed in <see cref="IPluginCatalog.BuiltInModules"/>.
/// </summary>
/// <param name="Id">Stable ID, same character rules as a plugin ID. Reserved: no external plugin may use it.</param>
/// <param name="Name">Name shown in the plugin list.</param>
/// <param name="Description">Optional one-line description.</param>
/// <param name="Assembly">The module's assembly; its version is shown in the plugin list. Pass <c>typeof(SomethingInTheModule).Assembly</c>.</param>
public sealed record BuiltInModule(string Id, string Name, string? Description = null, System.Reflection.Assembly? Assembly = null);

/// <summary>What a built-in module registered, for the plugin list and <c>toucan plugins list</c>.</summary>
/// <param name="Capabilities">The plugin capabilities the module contributes to (see <see cref="PluginCapabilities"/>).</param>
/// <param name="Registered">Counts per kind, e.g. <c>save-formats:14</c>, taken from the services the module added.</param>
/// <param name="Version">Version of the module's assembly (the app version for shipped modules), or empty if none was given.</param>
public sealed record BuiltInModuleInfo(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Registered,
    string Version = "");

public static class BuiltInModuleServiceCollectionExtensions
{
    // Order here is the order shown in Registered.
    private static readonly (Type Service, string Kind, string Capability)[] s_kinds =
    [
        (typeof(ISaveStrategy), "save-formats", PluginCapabilities.Formats),
        (typeof(ILoadStrategy), "load-formats", PluginCapabilities.Formats),
        (typeof(ITranslationProvider), "providers", PluginCapabilities.Providers),
        (typeof(IAiBackend), "ai-services", PluginCapabilities.Providers),
        (typeof(IValidationRule), "rules", PluginCapabilities.Validation),
        (typeof(IFrameworkProfile), "profiles", PluginCapabilities.Frameworks),
    ];

    /// <summary>
    /// Runs <paramref name="register"/> against <paramref name="services"/> and records what it added as a
    /// built-in module. What the module registered is read from the services it added, so the description
    /// cannot drift from the registrations. Call it after <c>AddToucanCore</c> has set up the shared services
    /// the module needs, and before <c>AddToucanPlugins</c> so plugins cannot take the module's IDs.
    /// </summary>
    public static IServiceCollection AddToucanModule(this IServiceCollection services, BuiltInModule module, Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(register);

        if (string.IsNullOrWhiteSpace(module.Id))
            throw new ArgumentException("A built-in module needs a non-empty Id.", nameof(module));
        if (ModulesIn(services).Any(m => string.Equals(m.Id, module.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Built-in module '{module.Id}' is already registered.");

        var before = services.Count;
        register(services);

        var added = services.Skip(before).Select(d => d.ServiceType).ToList();
        var registered = new List<string>();
        var capabilities = new List<string>();
        foreach (var (service, kind, capability) in s_kinds)
        {
            var count = added.Count(t => t == service);
            if (count == 0) continue;
            registered.Add($"{kind}:{count}");
            if (!capabilities.Contains(capability)) capabilities.Add(capability);
        }

        services.AddSingleton(new BuiltInModuleInfo(module.Id, module.Name, module.Description, capabilities, registered, VersionOf(module.Assembly)));
        return services;
    }

    /// <summary>
    /// Registers a format strategy once as its concrete type and forwards <typeparamref name="TService"/> to that
    /// instance, so other strategies can depend on the concrete type (<c>NamespacedLoadStrategy</c> needs <c>JsonLoadStrategy</c>).
    /// </summary>
    public static IServiceCollection AddFormatStrategy<TService, TImpl>(this IServiceCollection services)
        where TService : class
        where TImpl : class, TService
    {
        services.AddSingleton<TImpl>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImpl>());
        return services;
    }

    // "0.19.0+abc123" (source-link build metadata) is shown as "0.19.0".
    private static string VersionOf(System.Reflection.Assembly? assembly)
    {
        if (assembly is null) return string.Empty;
        var informational = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly))?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? string.Empty;
    }

    /// <summary>The built-in modules registered so far, in registration order.</summary>
    internal static IReadOnlyList<BuiltInModuleInfo> ModulesIn(IServiceCollection services) =>
        [.. services
            .Where(d => d.ServiceType == typeof(BuiltInModuleInfo))
            .Select(d => d.ImplementationInstance)
            .OfType<BuiltInModuleInfo>()];
}
