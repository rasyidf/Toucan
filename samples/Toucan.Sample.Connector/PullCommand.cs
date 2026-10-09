using Toucan.Plugins;

namespace Toucan.Sample.Connector;

/// <summary>Fills the project from the service. Only translations the user has not edited since the snapshot are touched.</summary>
public sealed class PullCommand(IPluginServices services, IRemoteSource remote, ConnectorSessions sessions) : ICommandHandler
{
    public static CommandDefinition Definition { get; } = new()
    {
        Id = "sample.connector.pull",
        Title = "Pull From Server",
        Category = "Sample Connector",
        Description = "Copies changed translations from the server into the open project, as one undoable edit.",
        Placements = CommandPlacement.CommandPalette | CommandPlacement.Menu,
        DefaultShortcut = "Mod+Alt+L",
    };

    public CommandState GetState(ICommandContext context) => ConnectorState.Of(context, sessions);

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        var connection = sessions.First!;
        invocation.Progress.Report(new CommandProgressInfo("Reading from the server", 0.1));
        var units = await remote.PullAsync(connection, cancellationToken).ConfigureAwait(false);
        var snapshot = await services.Workspace.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return;

        var edit = new WorkspaceEdit { Label = "Pulled from server", BasedOnRevision = snapshot.Revision };
        foreach (var unit in units)
        {
            if (snapshot.Find(unit.Key, unit.Language) is not { } local || local.Value == unit.Text) continue;
            edit.SetValue(unit.Key, unit.Language, unit.Text, expectedValue: local.Value);
        }

        invocation.Progress.Report(new CommandProgressInfo("Applying", 0.8));
        if (edit.Operations.Count == 0)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Up to date", Severity = NotificationSeverity.Info });
            return;
        }

        var result = await services.Workspace.ApplyAsync(edit, cancellationToken).ConfigureAwait(false);
        services.Notifier.Notify(result.Applied
            ? new PluginNotification { Title = "Pulled", Message = $"{result.ChangedUnits} translations updated. Undo restores them.", Severity = NotificationSeverity.Success }
            : new PluginNotification { Title = "Nothing was changed", Message = result.Issues.FirstOrDefault()?.Message, Severity = NotificationSeverity.Warning });
    }
}
