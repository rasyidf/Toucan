using Toucan.Plugins;

namespace Toucan.Sample.Connector;

/// <summary>Sends translations the user changed (and the server has not changed meanwhile) to the service.</summary>
public sealed class PushCommand(IPluginServices services, IRemoteSource remote, ConnectorSessions sessions) : ICommandHandler
{
    public static CommandDefinition Definition { get; } = new()
    {
        Id = "sample.connector.push",
        Title = "Push To Server",
        Category = "Sample Connector",
        Description = "Sends modified translations to the server. Anything the server changed meanwhile is skipped and reported.",
        Placements = CommandPlacement.CommandPalette | CommandPlacement.Menu,
    };

    public CommandState GetState(ICommandContext context) => ConnectorState.Of(context, sessions);

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        var connection = sessions.First!;
        var snapshot = await services.Workspace.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return;

        var remoteUnits = (await remote.PullAsync(connection, cancellationToken).ConfigureAwait(false))
            .ToDictionary(u => (u.Key, u.Language));
        var modified = snapshot.Units.Where(u => u.IsModified && u.Value.Length > 0).ToList();
        int sent = 0, skipped = 0;
        for (var i = 0; i < modified.Count; i++)
        {
            invocation.Progress.Report(new CommandProgressInfo($"Sending {i + 1} of {modified.Count}", (double)i / modified.Count));
            var u = modified[i];
            var known = remoteUnits.GetValueOrDefault((u.Key, u.Language));
            if (await remote.PushAsync(connection, new RemoteUnit(u.Key, u.Language, u.Value, 0), known?.Revision ?? 0, cancellationToken).ConfigureAwait(false)) sent++;
            else skipped++;
        }

        services.Notifier.Notify(new PluginNotification
        {
            Title = skipped == 0 ? "Pushed" : "Pushed with skipped items",
            Message = $"{sent} sent, {skipped} skipped because the server changed.",
            Severity = skipped == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning,
        });
    }
}
