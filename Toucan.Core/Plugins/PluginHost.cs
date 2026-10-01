using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// Discovers plugin folders, loads each into its own <see cref="PluginLoadContext"/>, runs
/// <see cref="IToucanPlugin.Initialize"/> and returns what each plugin registered. One bad plugin never stops
/// the others or the application: every failure becomes a <see cref="PluginLoadResult"/>.
/// </summary>
public sealed class PluginHost(PluginHostOptions options, ILoggerFactory? loggerFactory = null)
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    /// <summary>
    /// Loads every plugin and applies the accepted ones to <paramref name="services"/>.
    /// <paramref name="services"/> should already contain the built-ins (<c>AddToucanCore</c>) and logging.
    /// </summary>
    internal IReadOnlyList<PluginLoadResult> LoadInto(IServiceCollection services)
    {
        var folders = DiscoverFolders().ToList();
        if (folders.Count == 0) return [];

        var reserved = ReadReservedIds(services);
        var results = new List<PluginLoadResult>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
            results.Add(LoadOne(folder, services, reserved, seen));

        return results;
    }

    private IEnumerable<string> DiscoverFolders() =>
        options.Roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            .Where(dir => File.Exists(Path.Combine(dir, PluginManifest.FileName)));

    private PluginLoadResult LoadOne(string folder, IServiceCollection services, ReservedIds reserved, Dictionary<string, string> seen)
    {
        var log = _loggerFactory.CreateLogger<PluginHost>();

        PluginManifest manifest;
        try
        {
            var json = File.ReadAllText(Path.Combine(folder, PluginManifest.FileName));
            if (!PluginManifest.TryParse(json, out manifest, out var errors))
                return Reject(log, folder, null, PluginStatus.Rejected, string.Join(" ", errors));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Reject(log, folder, null, PluginStatus.Failed, $"Cannot read {PluginManifest.FileName}: {ex.Message}");
        }

        if (seen.TryGetValue(manifest.Id, out var firstFolder))
            return Reject(log, folder, manifest, PluginStatus.Rejected, $"Plugin ID '{manifest.Id}' is already used by '{Path.GetFileName(firstFolder)}'.");
        seen[manifest.Id] = folder;

        if ((options.IsEnabled is { } isEnabled && !isEnabled(manifest.Id)) || (options.Policy is { } policyCheck && !policyCheck.IsEnabled(manifest.Id)))
        {
            if (log.IsEnabled(LogLevel.Information))
                log.LogInformation("Plugin {Id} is disabled; skipping.", manifest.Id);
            return new PluginLoadResult(folder, PluginStatus.Disabled, manifest);
        }

        var apiVersion = manifest.ParsedApiVersion!;
        if (!PluginApi.IsCompatible(apiVersion))
            return Reject(log, folder, manifest, PluginStatus.Rejected,
                $"Built for plugin API {apiVersion}, but this Toucan implements {PluginApi.Current}.");

        string hash;
        try
        {
            hash = PluginHasher.Compute(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Reject(log, folder, manifest, PluginStatus.Failed, $"Cannot read plugin files: {ex.Message}");
        }

        var signature = options.SignatureVerifier.Verify(manifest, folder);
        if (signature == PluginSignatureStatus.Invalid)
            return Reject(log, folder, manifest, PluginStatus.Rejected, "Its signature is invalid.", hash, signature);

        PluginTrustState? trust = null;
        if (options.Policy is { } policy)
        {
            trust = policy.GetTrust(manifest.Id, hash);
            if (trust != PluginTrustState.Trusted && !options.AllowForThisRun.Contains(manifest.Id))
            {
                var why = trust == PluginTrustState.Changed
                    ? "its files have changed since you trusted it"
                    : "you have not trusted it yet";
                if (log.IsEnabled(LogLevel.Information))
                    log.LogInformation("Plugin {Id} was not loaded: {Why}.", manifest.Id, why);
                return new PluginLoadResult(folder, PluginStatus.NeedsTrust, manifest,
                    $"Not loaded: {why}.", ContentHash: hash, Signature: signature, Trust: trust);
            }
        }

        var loaded = Activate(log, folder, manifest, services, reserved);
        return loaded with { ContentHash = hash, Signature = signature, Trust = trust };
    }

    // Plugin code is untrusted: any exception it (or its static constructors, or assembly loading) throws is a
    // load failure for that plugin only, so catching Exception is deliberate here.
#pragma warning disable CA1031
    private PluginLoadResult Activate(ILogger log, string folder, PluginManifest manifest, IServiceCollection services, ReservedIds reserved)
    {
        try
        {
            var entryPath = Path.GetFullPath(Path.Combine(folder, manifest.EntryAssembly));
            if (!File.Exists(entryPath))
                return Reject(log, folder, manifest, PluginStatus.Failed, $"Entry assembly '{manifest.EntryAssembly}' was not found.");

            var loadContext = new PluginLoadContext(manifest.Id, entryPath, options.SharedAssemblies);
            var assembly = loadContext.LoadFromAssemblyPath(entryPath);
            var pluginType = FindPluginType(assembly, manifest.EntryType, out var typeError);
            if (pluginType is null)
                return Reject(log, folder, manifest, PluginStatus.Failed, typeError!);

            var plugin = (IToucanPlugin)Activator.CreateInstance(pluginType)!;
            var context = new PluginContext(manifest, folder, _loggerFactory.CreateLogger($"Plugin.{manifest.Id}"), reserved);
            plugin.Initialize(context);

            context.Commit();
            Apply(services, context);

            if (log.IsEnabled(LogLevel.Information))
                log.LogInformation("Loaded plugin {Id} {Version} ({Summary}).", manifest.Id, manifest.Version, string.Join(", ", context.Summary));
            return new PluginLoadResult(folder, PluginStatus.Loaded, manifest, Registered: context.Summary);
        }
        catch (PluginRegistrationException ex)
        {
            return Reject(log, folder, manifest, PluginStatus.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Plugin {Id} failed to load.", manifest.Id);
            return new PluginLoadResult(folder, PluginStatus.Failed, manifest, Describe(ex));
        }
    }
#pragma warning restore CA1031

    private static Type? FindPluginType(Assembly assembly, string? entryType, out string? error)
    {
        error = null;
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        var candidates = types
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IToucanPlugin).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        if (entryType is { Length: > 0 })
        {
            var match = candidates.FirstOrDefault(t => string.Equals(t.FullName, entryType, StringComparison.Ordinal));
            if (match is null) error = $"'{entryType}' is not a public IToucanPlugin with a parameterless constructor in {assembly.GetName().Name}.";
            return match;
        }

        switch (candidates.Count)
        {
            case 1:
                return candidates[0];
            case 0:
                error = $"{assembly.GetName().Name} has no public IToucanPlugin implementation with a parameterless constructor.";
                return null;
            default:
                error = $"{assembly.GetName().Name} has {candidates.Count} IToucanPlugin implementations ({string.Join(", ", candidates.Select(c => c.Name))}); set 'entryType' in the manifest.";
                return null;
        }
    }

    private static void Apply(IServiceCollection services, PluginContext context)
    {
        foreach (var (save, load) in context.Formats)
        {
            services.AddSingleton(save);
            services.AddSingleton(load);
        }
        foreach (var provider in context.Providers) services.AddSingleton(provider);
        foreach (var rule in context.Rules) services.AddSingleton(rule);
        foreach (var profile in context.Profiles) services.AddSingleton(profile);
    }

    /// <summary>Reads the IDs the built-ins already use from a throwaway container built from the current registrations.</summary>
    private static ReservedIds ReadReservedIds(IServiceCollection services)
    {
        using var probe = services.BuildServiceProvider();
        var reserved = new ReservedIds();
        reserved.Formats.UnionWith(probe.GetServices<ISaveStrategy>().Select(s => s.FormatId));
        reserved.Formats.UnionWith(probe.GetServices<ILoadStrategy>().Select(s => s.FormatId));
        reserved.Providers.UnionWith(probe.GetServices<ITranslationProvider>().Select(p => p.Name));
        reserved.Rules.UnionWith(probe.GetServices<IValidationRule>().Select(r => r.Id));
        reserved.Profiles.UnionWith(probe.GetServices<IFrameworkProfile>().Select(p => p.Id));
        return reserved;
    }

    private static PluginLoadResult Reject(ILogger log, string folder, PluginManifest? manifest, PluginStatus status, string error,
        string? hash = null, PluginSignatureStatus signature = PluginSignatureStatus.NotSigned)
    {
        if (log.IsEnabled(LogLevel.Warning))
            log.LogWarning("Plugin {Folder} was not loaded: {Error}", Path.GetFileName(folder), error);
        return new PluginLoadResult(folder, status, manifest, error, ContentHash: hash, Signature: signature);
    }

    private static string Describe(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? Describe(inner) : $"{ex.GetType().Name}: {ex.Message}";
}

public static class PluginServiceCollectionExtensions
{
    /// <summary>
    /// Loads plugins and adds what they register to <paramref name="services"/>. Call after <c>AddToucanCore</c>
    /// (and after logging is registered) and before building the container. Registers an <see cref="IPluginCatalog"/>
    /// describing every plugin that was found, loaded or not.
    /// </summary>
    public static IServiceCollection AddToucanPlugins(this IServiceCollection services, PluginHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // The logger factory belongs to this throwaway container, so keep it alive until loading is finished.
        using var probe = services.BuildServiceProvider();
        var results = new PluginHost(options, probe.GetService<ILoggerFactory>()).LoadInto(services);
        services.AddSingleton<IPluginCatalog>(new PluginCatalog(results));
        return services;
    }
}
