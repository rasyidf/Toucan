using Toucan.Plugins;

namespace Toucan.Sample.Plugin;

/// <summary>
/// A command that needs an open project. It starts a background operation (progress and a cancel button in the status bar),
/// appends to a log in the plugin's own data folder, and tells the user when it is done.
/// </summary>
public sealed class StampCommand(IPluginServices services) : ICommandHandler
{
    public static CommandDefinition Definition { get; } = new()
    {
        Id = "sample.tsv.stamp",
        Title = "Write Sample Stamp",
        LocalizedTitles = new Dictionary<string, string> { ["id"] = "Tulis Stempel Contoh" },
        Category = "Sample",
        Description = "Writes the project path and time to a log in the plugin's data folder.",
        DefaultShortcut = "Mod+Alt+K",
        RequiresWorkspace = true,
        Icon = "Edit",
    };

    public const string LogFile = "stamps.log";

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var workspaceId = invocation.Context.WorkspaceId;
        var greeting = services.Configuration.GetValue<string>("greeting") ?? "Hello";

        var operation = services.Operations.Start(new OperationOptions { Title = "Writing sample stamp" }, async (ctx, ct) =>
        {
            for (var step = 1; step <= 3; step++)
            {
                ctx.Report($"Step {step} of 3", step / 3.0);
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            var existing = await services.Storage.ReadTextAsync(LogFile, ct).ConfigureAwait(false) ?? string.Empty;
            await services.Storage.WriteTextAsync(LogFile, existing + $"{DateTimeOffset.Now:O}  {greeting}  {workspaceId}{Environment.NewLine}", ct).ConfigureAwait(false);
        });
        // The command ends with the operation, so cancelling the command (or closing the project) stops it too.
        using var link = cancellationToken.Register(operation.Cancel);
        await operation.Completion.ConfigureAwait(false);

        if (operation.Status == OperationStatus.Completed)
        {
            services.Diagnostics.Write(DiagnosticLevel.Info, $"Stamp written for {workspaceId}.");
            services.Notifier.Notify(new PluginNotification
            {
                Title = "Sample stamp written",
                Message = $"{greeting}! Saved to {LogFile} in the plugin's data folder.",
                Severity = NotificationSeverity.Success,
            });
        }
        else if (operation.Status == OperationStatus.Failed)
        {
            services.Notifier.Notify(new PluginNotification { Title = "Sample stamp failed", Message = operation.FailureMessage, Severity = NotificationSeverity.Error });
        }
    }
}
