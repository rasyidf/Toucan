using Microsoft.Extensions.Logging;

namespace Toucan.Plugins;

/// <summary>How long an activation lives. Wider scopes outlive narrower ones and are torn down last.</summary>
public enum PluginLifetime
{
    /// <summary>The whole application run.</summary>
    Application,

    /// <summary>One open project; ends when the project closes.</summary>
    Workspace,

    /// <summary>One connection inside a workspace; ends when it is removed or its workspace closes.</summary>
    Connection,
}

/// <summary>What the host tells an activator about the scope it is being activated for.</summary>
public interface IPluginActivationContext
{
    string PluginId { get; }
    PluginLifetime Lifetime { get; }

    /// <summary>The open project this activation belongs to; null for <see cref="PluginLifetime.Application"/>.</summary>
    string? WorkspaceId { get; }

    /// <summary>The connection this activation belongs to; non-null only for <see cref="PluginLifetime.Connection"/>.</summary>
    string? ConnectionId { get; }

    /// <summary>Logger scoped to this plugin.</summary>
    ILogger Logger { get; }

    /// <summary>Cancelled when the scope ends. Pass it to anything long-running the activation starts.</summary>
    CancellationToken Scope { get; }
}

/// <summary>
/// Turns a registered capability on for one scope. Registering an activator (in <see cref="IToucanPlugin.Initialize"/>)
/// must not touch the network or the disk beyond reading the plugin's own files; everything with side effects
/// happens here. Each call gets its own session, so two connections never share state by accident.
/// </summary>
public interface IPluginActivator
{
    /// <summary>Which scope this activator is for.</summary>
    PluginLifetime Lifetime { get; }

    /// <summary>
    /// Starts the session. The returned object is disposed when the scope ends (after <see cref="IPluginActivationContext.Scope"/>
    /// is cancelled); return null if there is nothing to dispose. Throwing marks only this activation as failed.
    /// </summary>
    Task<IAsyncDisposable?> ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken);
}
