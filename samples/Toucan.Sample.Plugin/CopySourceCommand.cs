using Toucan.Plugins;

namespace Toucan.Sample.Plugin;

/// <summary>
/// Copies the source text into every empty translation, as a first draft to work from. It shows the shape of an edit through
/// <see cref="IWorkspaceApi"/>: take a snapshot, prepare the changes on it, and apply them together, refusing them if the
/// project moved on meanwhile.
/// </summary>
public sealed class CopySourceCommand(IPluginServices services) : ICommandHandler
{
    public static CommandDefinition Definition { get; } = new()
    {
        Id = "sample.tsv.copy-source",
        Title = "Copy Source Into Empty Translations",
        Category = "Sample",
        Description = "Fills every empty translation with the source text, in one step you can undo.",
        RequiresWorkspace = true,
    };

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        var workspace = services.Workspace;
        var snapshot = await workspace.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return;

        var edit = new WorkspaceEdit { Label = "Copied source text", BasedOnRevision = snapshot.Revision };
        foreach (var source in snapshot.Units.Where(u => u.Language == snapshot.PrimaryLanguage && u.Value.Length > 0))
        {
            foreach (var language in snapshot.Languages.Where(l => l != snapshot.PrimaryLanguage))
            {
                if (snapshot.Find(source.Key, language) is { Value.Length: 0 })
                    edit.SetValue(source.Key, language, source.Value, expectedValue: string.Empty);
            }
        }

        if (edit.Operations.Count == 0)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Nothing to copy", Message = "Every translation already has text.", Severity = NotificationSeverity.Info });
            return;
        }

        var result = await workspace.ApplyAsync(edit, cancellationToken).ConfigureAwait(false);
        services.Notifier.Notify(result.Applied
            ? new PluginNotification { Title = "Source text copied", Message = $"{result.ChangedUnits} translations filled. Undo restores them.", Severity = NotificationSeverity.Success }
            : new PluginNotification { Title = "Nothing was changed", Message = result.Issues.FirstOrDefault()?.Message, Severity = NotificationSeverity.Warning });
    }
}
