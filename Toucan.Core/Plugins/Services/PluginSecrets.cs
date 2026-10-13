using Toucan.Core.Contracts;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>A plugin's slice of the host secret store. Every value that passes through is registered for masking.</summary>
internal sealed class PluginSecrets(string pluginId, Func<ISecretService> secrets, IDiagnosticsService diagnostics) : IPluginSecrets
{
    /// <summary>The secret store key for a name and target, so settings and plugins name a secret the same way.</summary>
    public string KeyFor(string name, ConfigTarget? target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var t = target ?? ConfigTarget.App;
        var scope = t.Scope switch
        {
            ConfigScope.App => "app",
            ConfigScope.Workspace => $"ws-{SecretKeys.ProjectId(Required(t.WorkspaceId, "workspace"))}",
            _ => (t.WorkspaceId is { Length: > 0 } w ? $"ws-{SecretKeys.ProjectId(w)}/" : string.Empty)
                 + $"conn-{Required(t.ConnectionId, "connection").Trim().Replace('/', '_').ToLowerInvariant()}",
        };
        return SecretKeys.Plugin(pluginId, scope, name);
    }

    public Task<string?> GetAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        var value = secrets().GetSecret(KeyFor(name, target));
        if (value is not null) diagnostics.RegisterSecret(value);
        return Task.FromResult(value);
    }

    public Task SetAsync(string name, string? value, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(value)) diagnostics.RegisterSecret(value);
        secrets().SetSecret(KeyFor(name, target), value);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(secrets().Remove(KeyFor(name, target)));

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"This target needs a {what} ID.") : value;
}
