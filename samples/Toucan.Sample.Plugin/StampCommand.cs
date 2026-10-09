using Toucan.Plugins;

namespace Toucan.Sample.Plugin;

/// <summary>
/// A command that needs an open project. It reports progress in three steps and honours cancellation, then writes a
/// small text file so there is something to see until the host shows command progress and notifications.
/// </summary>
public sealed class StampCommand : ICommandHandler
{
    public static CommandDefinition Definition { get; } = new()
    {
        Id = "sample.tsv.stamp",
        Title = "Write Sample Stamp",
        LocalizedTitles = new Dictionary<string, string> { ["id"] = "Tulis Stempel Contoh" },
        Category = "Sample",
        Description = "Writes the project path and time to a file in the temp folder.",
        DefaultShortcut = "Mod+Alt+K",
        RequiresWorkspace = true,
        Icon = "Edit",
    };

    public static string OutputPath { get; } = Path.Combine(Path.GetTempPath(), "toucan-sample-stamp.txt");

    public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        for (var step = 1; step <= 3; step++)
        {
            invocation.Progress.Report(new CommandProgressInfo($"Step {step} of 3", step / 3.0));
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        await File.AppendAllTextAsync(OutputPath, $"{DateTimeOffset.Now:O}  {invocation.Context.WorkspaceId}{Environment.NewLine}", cancellationToken).ConfigureAwait(false);
    }
}
