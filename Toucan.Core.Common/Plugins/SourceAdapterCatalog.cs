using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Plugins;

/// <summary>A content-source adapter and who provides it: a plugin's ID, or <see cref="BuiltInOwner"/> for a built-in module.</summary>
public sealed record RegisteredSourceAdapter(string PluginId, IContentSourceAdapter Adapter)
{
    /// <summary>The owner recorded for adapters that ship with Toucan.</summary>
    public const string BuiltInOwner = "toucan";

    public SourceAdapterDescriptor Descriptor => Adapter.Descriptor;
    public bool IsBuiltIn => PluginId == BuiltInOwner;
}

/// <summary>Every content-source adapter this host can use, from built-in modules and loaded plugins.</summary>
public interface ISourceAdapterCatalog
{
    IReadOnlyList<RegisteredSourceAdapter> Adapters { get; }

    /// <summary>The adapter with this ID, or null when no loaded plugin provides it (the plugin is missing, disabled or untrusted).</summary>
    RegisteredSourceAdapter? Find(string adapterId);
}

internal sealed class SourceAdapterCatalog : ISourceAdapterCatalog
{
    private readonly Dictionary<string, RegisteredSourceAdapter> _byId;

    public SourceAdapterCatalog(IEnumerable<RegisteredSourceAdapter> adapters)
    {
        Adapters = [.. adapters.OrderBy(a => a.Descriptor.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        _byId = new(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in Adapters)
            if (!_byId.TryAdd(adapter.Descriptor.Id, adapter))
                throw new InvalidOperationException($"Two source adapters use the ID '{adapter.Descriptor.Id}'.");
    }

    public IReadOnlyList<RegisteredSourceAdapter> Adapters { get; }

    public RegisteredSourceAdapter? Find(string adapterId) => _byId.GetValueOrDefault(adapterId);
}

public static class SourceAdapterServiceCollectionExtensions
{
    /// <summary>Registers the catalog. <c>AddToucanCore</c> calls this; plugins add adapters through <c>IPluginContext</c>.</summary>
    public static IServiceCollection AddToucanSourceCatalog(this IServiceCollection services)
    {
        services.TryAddSingleton<ISourceAdapterCatalog, SourceAdapterCatalog>();
        return services;
    }

    /// <summary>Adds an adapter that ships with Toucan (a built-in module). Its ID must start with <c>toucan.</c>.</summary>
    public static IServiceCollection AddBuiltInSourceAdapter(this IServiceCollection services, IContentSourceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!adapter.Descriptor.Id.StartsWith(RegisteredSourceAdapter.BuiltInOwner + ".", StringComparison.Ordinal))
            throw new ArgumentException($"Built-in adapter IDs start with '{RegisteredSourceAdapter.BuiltInOwner}.'.", nameof(adapter));
        services.AddSingleton(new RegisteredSourceAdapter(RegisteredSourceAdapter.BuiltInOwner, adapter));
        return services.AddToucanSourceCatalog();
    }
}
