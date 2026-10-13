using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// Hands out host services once the application container exists. Plugins register during startup, before the container is
/// built, so their service objects keep a reference to this and look things up on first use.
/// </summary>
internal sealed class PluginServicesBroker
{
    private IServiceProvider? _provider;
    private event Action? Attached;

    public bool IsAttached => _provider is not null;

    public void Attach(IServiceProvider provider)
    {
        _provider = provider;
        Attached?.Invoke();
    }

    /// <summary>Runs <paramref name="action"/> once the container is connected: now if it already is.</summary>
    public void WhenAttached(Action action)
    {
        if (_provider is not null) action();
        else Attached += action;
    }

    /// <summary>A service the host may or may not provide.</summary>
    public T? TryGet<T>() where T : class => _provider?.GetService<T>();

    public T Require<T>() where T : notnull =>
        (_provider ?? throw new InvalidOperationException(
            "Plugin services are not ready yet: use them after startup, not while the plugin registers. A host calls UsePluginServices once its container is built."))
        .GetRequiredService<T>();
}

/// <summary>The services shared by every plugin, created before plugins load so their loggers and notifier can use them from the start.</summary>
internal sealed class PluginHostServices
{
    public PluginServicesBroker Broker { get; } = new();
    public IDiagnosticsService Diagnostics { get; }
    public INotificationCenter Notifications { get; }
    public IBackgroundOperationService Operations { get; }

    public PluginHostServices()
    {
        var diagnostics = new DiagnosticsService();
        Diagnostics = diagnostics;
        Notifications = new NotificationCenter(diagnostics);
        Operations = new BackgroundOperationService(diagnostics);
    }
}

/// <summary>One plugin's services; also what the desktop app reads to build the plugin's settings form.</summary>
public sealed record PluginServicesRegistration(string PluginId, IPluginServices Services);

internal sealed class PluginServices : IPluginServices
{
    public PluginServices(string pluginId, PluginHostServices host, string dataRoot)
    {
        var storage = new PluginStorage(dataRoot, pluginId);
        var secrets = new PluginSecrets(pluginId, host.Broker.Require<ISecretService>, host.Diagnostics);
        Storage = storage;
        Secrets = secrets;
        PluginConfiguration = new PluginConfiguration(pluginId, storage, secrets, host.Diagnostics);
        Notifier = new PluginNotifier(pluginId, host.Notifications);
        Operations = new PluginOperations(pluginId, host.Operations);
        Diagnostics = new PluginDiagnostics(pluginId, host.Diagnostics);
        Workspace = new PluginWorkspaceApi(pluginId, host.Broker);
    }

    public IPluginStorage Storage { get; }
    public IPluginConfiguration Configuration => PluginConfiguration;
    public PluginConfiguration PluginConfiguration { get; }
    public IPluginSecrets Secrets { get; }
    public IPluginNotifier Notifier { get; }
    public IBackgroundOperations Operations { get; }
    public IPluginDiagnostics Diagnostics { get; }
    public IWorkspaceApi Workspace { get; }
}

internal sealed class PluginNotifier(string pluginId, INotificationCenter center) : IPluginNotifier
{
    public void Notify(PluginNotification notification) => center.Publish(pluginId, notification);
}

internal sealed class PluginDiagnostics(string pluginId, IDiagnosticsService service) : IPluginDiagnostics
{
    public string Redact(string text) => service.Redact(text);
    public void Write(DiagnosticLevel level, string message) => service.Write(pluginId, level, message);
    public void RegisterSecret(string value) => service.RegisterSecret(value);
}

public static class PluginServicesServiceProviderExtensions
{
    /// <summary>
    /// Connects the plugins' services to the built container. Call once, right after building it (the desktop app and the CLI
    /// do); until then a plugin's services refuse to run.
    /// </summary>
    public static IServiceProvider UsePluginServices(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider.GetService<PluginHostServicesAccessor>()?.Attach(provider);
        return provider;
    }
}

/// <summary>Registered in the container so <see cref="PluginServicesServiceProviderExtensions.UsePluginServices"/> can reach the broker and the activation service.</summary>
internal sealed class PluginHostServicesAccessor(PluginHostServices host)
{
    public void Attach(IServiceProvider provider)
    {
        host.Broker.Attach(provider);
        // Work started for a project ends with it.
        if (provider.GetService<IPluginActivationService>() is { } activation)
            activation.WorkspaceClosed += (_, _) => host.Operations.CancelForWorkspaceClose();
    }
}
