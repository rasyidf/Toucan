using System.Reflection;
using System.Runtime.Loader;

namespace Toucan.Core.Plugins;

/// <summary>
/// Isolates one plugin's private dependencies. Assemblies that form the contract with the host (the abstractions,
/// Core, logging/DI abstractions) always resolve from the host's default context so type identity matches and a
/// plugin's <c>ISaveStrategy</c> is the host's <c>ISaveStrategy</c>.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string[] s_alwaysShared =
    [
        "Toucan.Plugins.Abstractions",
        "Toucan.Core",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    private readonly AssemblyDependencyResolver _resolver;
    private readonly ISet<string> _shared;
    private readonly string[] _sharedPrefixes;

    public PluginLoadContext(string pluginName, string entryAssemblyPath, ISet<string> extraShared, IEnumerable<string>? sharedPrefixes = null)
        : base($"plugin:{pluginName}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _shared = new HashSet<string>(s_alwaysShared.Concat(extraShared), StringComparer.OrdinalIgnoreCase);
        _sharedPrefixes = [.. sharedPrefixes ?? []];
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || _shared.Contains(assemblyName.Name)
            || _sharedPrefixes.Any(p => assemblyName.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return null; // fall back to the default context

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
