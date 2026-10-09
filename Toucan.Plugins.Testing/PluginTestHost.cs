using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;

namespace Toucan.Plugins.Testing;

/// <summary>
/// Runs a plugin the way Toucan does, without Toucan: <see cref="IToucanPlugin.Initialize"/> against a context that records what
/// the plugin registers, then activation per workspace or connection with a scope that is cancelled when that scope closes.
/// Registration touches no network: if the plugin reaches for one in <c>Initialize</c>, the test sees it hang or fail here.
/// </summary>
public sealed class PluginTestHost : IAsyncDisposable
{
    private readonly Dictionary<string, IPluginActivator> _activators = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(PluginLifetime Lifetime, string? Workspace, string? Connection, CancellationTokenSource Scope, IAsyncDisposable? Instance)> _active = [];

    public PluginTestHost(string pluginId = "test.plugin", ILogger? logger = null)
    {
        PluginId = pluginId;
        Services = new TestPluginServices();
        Context = new TestContext(this, logger ?? NullLogger.Instance);
    }

    public string PluginId { get; }
    public TestPluginServices Services { get; }
    public IPluginContext Context { get; }

    public IList<(ISaveStrategy Save, ILoadStrategy Load)> Formats { get; } = [];
    public IList<ITranslationProvider> Providers { get; } = [];
    public IList<IValidationRule> Rules { get; } = [];
    public IList<IFrameworkProfile> Profiles { get; } = [];
    public IDictionary<string, (CommandDefinition Definition, ICommandHandler Handler)> Commands { get; } = new Dictionary<string, (CommandDefinition, ICommandHandler)>(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="plugin"/>'s registration. Throws what the plugin throws, as the host would report a registration failure.</summary>
    public PluginTestHost Register(IToucanPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        plugin.Initialize(Context);
        return this;
    }

    /// <summary>The state of a registered command now, for a workspace that is open or not.</summary>
    public CommandState StateOf(string commandId) =>
        Commands[commandId] is var (definition, handler) ? StateFor(definition, handler) : default;

    private CommandState StateFor(CommandDefinition definition, ICommandHandler handler)
    {
        var context = new Ctx(Services.Workspace.IsOpen, Services.Workspace.IsOpen ? "test-workspace" : null);
        if (definition.RequiresWorkspace && !context.HasWorkspace) return new CommandState(CommandAvailability.Unavailable, "No project is open.");
        return handler.GetState(context);
    }

    /// <summary>Runs a command like the palette would, with an invocation you can read the progress of.</summary>
    public async Task<IReadOnlyList<CommandProgressInfo>> RunAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default)
    {
        var (definition, handler) = Commands[commandId];
        var state = StateFor(definition, handler);
        if (!state.CanRun) throw new InvalidOperationException($"'{commandId}' cannot run: {state.Availability}{(state.Reason is null ? string.Empty : " - " + state.Reason)}");
        var progress = new CollectingProgress();
        await handler.ExecuteAsync(new Invocation(new Ctx(Services.Workspace.IsOpen, "test-workspace"), parameter, progress), cancellationToken).ConfigureAwait(false);
        return progress.Items;
    }

    /// <summary>Activates every activator of <paramref name="lifetime"/>. The returned scope token is cancelled by <see cref="CloseAsync"/>.</summary>
    public async Task ActivateAsync(PluginLifetime lifetime, string? workspaceId = null, string? connectionId = null, CancellationToken cancellationToken = default)
    {
        foreach (var activator in _activators.Values.Where(a => a.Lifetime == lifetime))
        {
            var scope = new CancellationTokenSource();
            var context = new ActivationContext(PluginId, lifetime, workspaceId, connectionId, NullLogger.Instance, scope.Token);
            var instance = await activator.ActivateAsync(context, cancellationToken).ConfigureAwait(false);
            _active.Add((lifetime, workspaceId, connectionId, scope, instance));
        }
    }

    public int ActiveCount => _active.Count;

    /// <summary>Closes a workspace or connection: cancels its scope, cancels its background operations, and disposes what its activators returned.</summary>
    public async Task CloseAsync(PluginLifetime lifetime, string? workspaceId = null, string? connectionId = null)
    {
        foreach (var entry in _active.Where(a => a.Lifetime == lifetime && a.Workspace == workspaceId && a.Connection == connectionId).ToList())
        {
            await entry.Scope.CancelAsync().ConfigureAwait(false);
            if (entry.Instance is not null) await entry.Instance.DisposeAsync().ConfigureAwait(false);
            _active.Remove(entry);
        }
        if (lifetime == PluginLifetime.Workspace)
        {
            Services.BackgroundOperations.CancelAll();
            Services.InMemory.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _active.ToList())
            await CloseAsync(entry.Lifetime, entry.Workspace, entry.Connection).ConfigureAwait(false);
        Services.Dispose();
    }

    private sealed class TestContext(PluginTestHost host, ILogger logger) : IPluginContext
    {
        public Version HostApiVersion => new(1, 1);
        public string PluginDirectory => host.Services.Storage.DataDirectory;
        public ILogger Logger { get; } = logger;
        public IPluginServices Services => host.Services;
        public void AddFormat(ISaveStrategy save, ILoadStrategy load) => host.Formats.Add((save, load));
        public void AddProvider(ITranslationProvider provider) => host.Providers.Add(provider);
        public void AddValidationRule(IValidationRule rule) => host.Rules.Add(rule);
        public void AddFrameworkProfile(IFrameworkProfile profile) => host.Profiles.Add(profile);
        public void AddActivator(string id, IPluginActivator activator) => host._activators.Add(id, activator);
        public void SetConfiguration(ConfigSchema schema) => host.Services.Config.Schema = schema;

        public void AddCommand(CommandDefinition definition, ICommandHandler handler)
        {
            if (!host.Commands.TryAdd(definition.Id, (definition, handler)))
                throw new InvalidOperationException($"Command '{definition.Id}' is registered twice.");
        }
    }

    private sealed record Ctx(bool HasWorkspace, string? WorkspaceId) : ICommandContext;
    private sealed record Invocation(ICommandContext Context, object? Parameter, IProgress<CommandProgressInfo> Progress) : ICommandInvocation;
    private sealed record ActivationContext(string PluginId, PluginLifetime Lifetime, string? WorkspaceId, string? ConnectionId, ILogger Logger, CancellationToken Scope) : IPluginActivationContext;

    private sealed class CollectingProgress : IProgress<CommandProgressInfo>
    {
        private readonly List<CommandProgressInfo> _items = [];
        public IReadOnlyList<CommandProgressInfo> Items => _items;
        public void Report(CommandProgressInfo value) { lock (_items) _items.Add(value); }
    }
}

/// <summary>Checks that a plugin's own assemblies use no UI framework, which is what lets the CLI load it.</summary>
public static class CliCompatibility
{
    private static readonly string[] s_uiPrefixes = ["Avalonia", "FluentAvalonia", "Toucan.Plugins.Avalonia", "PresentationFramework", "WindowsBase", "System.Windows"];

    /// <summary>Names of referenced UI assemblies in <paramref name="pluginAssembly"/>; empty means the CLI can load it.</summary>
    public static IReadOnlyList<string> UiReferences(System.Reflection.Assembly pluginAssembly) =>
        [.. pluginAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => s_uiPrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))];
}
