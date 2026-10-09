using Toucan.Plugins;
using Toucan.Plugins.Testing;
using Toucan.Sample.Connector;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

public sealed class ConnectorHarnessTests : IAsyncDisposable
{
    private readonly InMemoryRemote _remote = new();
    private readonly PluginTestHost _host = new(ConnectorPlugin.Id);

    public ConnectorHarnessTests() => _host.Register(new ConnectorPlugin(_remote));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private void OpenProject(string source = "Save", string target = "")
    {
        _host.Services.InMemory.Open("en", ["en", "de"], new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["app.save"] = new Dictionary<string, string> { ["en"] = source, ["de"] = target },
            ["app.cancel"] = new Dictionary<string, string> { ["en"] = "Cancel", ["de"] = "Abbrechen" },
        });
    }

    [Fact]
    public void RegistrationRecordsCommandsAndSettingsWithoutActivatingAnything()
    {
        Assert.Equal(["sample.connector.pull", "sample.connector.push"], _host.Commands.Keys.Order());
        Assert.NotNull(_host.Services.Config.Schema);
        Assert.Equal(0, _host.ActiveCount);
    }

    [Fact]
    public void ThePluginReferencesNoUiAssembly() =>
        Assert.Empty(CliCompatibility.UiReferences(typeof(ConnectorPlugin).Assembly));

    [Fact]
    public async Task CommandsAreUnavailableWithoutAProjectAndDisconnectedWithoutAConnection()
    {
        Assert.Equal(CommandAvailability.Unavailable, _host.StateOf("sample.connector.pull").Availability);

        OpenProject();
        Assert.Equal(CommandAvailability.Disconnected, _host.StateOf("sample.connector.pull").Availability);

        await _host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");
        Assert.Equal(CommandAvailability.Available, _host.StateOf("sample.connector.pull").Availability);
    }

    [Fact]
    public async Task ClosingTheConnectionDisconnectsAndClosingTheWorkspaceMakesCommandsUnavailable()
    {
        OpenProject();
        await _host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");

        await _host.CloseAsync(PluginLifetime.Connection, connectionId: "c1");
        Assert.Equal(CommandAvailability.Disconnected, _host.StateOf("sample.connector.push").Availability);

        await _host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");
        await _host.CloseAsync(PluginLifetime.Workspace);
        Assert.Equal(CommandAvailability.Unavailable, _host.StateOf("sample.connector.push").Availability);
    }

    [Fact]
    public async Task PullFillsTheProjectInOneEditAndReportsIt()
    {
        OpenProject();
        _remote.Seed("c1", new RemoteUnit("app.save", "de", "Speichern", 1));
        await _host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");

        var progress = await _host.RunAsync("sample.connector.pull");

        var snapshot = await _host.Services.Workspace.SnapshotAsync();
        Assert.Equal("Speichern", snapshot!.Find("app.save", "de")!.Value);
        Assert.True(snapshot.Find("app.save", "de")!.IsModified);
        Assert.NotEmpty(progress);
        Assert.Contains(_host.Services.Notifications.Items, n => n.Title == "Pulled" && n.Severity == NotificationSeverity.Success);
    }

    [Fact]
    public async Task APullIsRefusedWhenTheUserEditedTheSameValueMeanwhile()
    {
        OpenProject();
        var snapshot = await _host.Services.Workspace.SnapshotAsync();
        _host.Services.InMemory.UserEdit("app.save", "de", "Sichern");

        var edit = new WorkspaceEdit { BasedOnRevision = snapshot!.Revision }.SetValue("app.save", "de", "Speichern", expectedValue: "");
        var result = await _host.Services.Workspace.ApplyAsync(edit);

        Assert.False(result.Applied);
        Assert.Equal(EditIssueKind.Stale, result.Issues[0].Kind);
        Assert.Equal("Sichern", (await _host.Services.Workspace.SnapshotAsync())!.Find("app.save", "de")!.Value);
    }

    [Fact]
    public async Task AnEditWithOneBadStepChangesNothing()
    {
        OpenProject();
        var edit = new WorkspaceEdit()
            .SetValue("app.save", "de", "Speichern")
            .SetValue("missing.key", "de", "x");

        var result = await _host.Services.Workspace.ApplyAsync(edit);

        Assert.False(result.Applied);
        Assert.Equal(EditIssueKind.UnknownKey, result.Issues[0].Kind);
        Assert.Equal("", (await _host.Services.Workspace.SnapshotAsync())!.Find("app.save", "de")!.Value);
    }

    [Fact]
    public async Task PushSendsModifiedValues()
    {
        OpenProject();
        _remote.Seed("c1", new RemoteUnit("app.cancel", "de", "Abbruch", 5));
        await _host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");
        _host.Services.InMemory.UserEdit("app.save", "de", "Speichern");
        _host.Services.InMemory.UserEdit("app.cancel", "de", "Abbrechen");

        await _host.RunAsync("sample.connector.push");

        Assert.Equal("Speichern", _remote.Get("c1", "app.save", "de")!.Text);
        Assert.Equal("Abbrechen", _remote.Get("c1", "app.cancel", "de")!.Text);
        Assert.Contains(_host.Services.Notifications.Items, n => n.Title == "Pushed");
    }

    [Fact]
    public async Task PushSkipsWhatTheServerChangedMeanwhile()
    {
        await using var host = new PluginTestHost(ConnectorPlugin.Id);
        host.Register(new ConnectorPlugin(new RacingRemote(_remote)));
        host.Services.InMemory.Open("en", ["en", "de"], new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["app.save"] = new Dictionary<string, string> { ["en"] = "Save", ["de"] = "" },
        });
        await host.ActivateAsync(PluginLifetime.Connection, connectionId: "c1");
        host.Services.InMemory.UserEdit("app.save", "de", "Speichern");

        await host.RunAsync("sample.connector.push");

        Assert.Null(_remote.Get("c1", "app.save", "de"));
        Assert.Contains(host.Services.Notifications.Items, n => n.Title == "Pushed with skipped items" && n.Severity == NotificationSeverity.Warning);
    }

    /// <summary>A service whose copy changes between the connector reading it and writing to it.</summary>
    private sealed class RacingRemote(InMemoryRemote inner) : IRemoteSource
    {
        public Task<IReadOnlyList<RemoteUnit>> PullAsync(string connectionId, CancellationToken cancellationToken) =>
            inner.PullAsync(connectionId, cancellationToken);

        public Task<bool> PushAsync(string connectionId, RemoteUnit unit, long expectedRevision, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    [Fact]
    public async Task ClosingTheWorkspaceCancelsBackgroundOperations()
    {
        var started = new TaskCompletionSource();
        var handle = _host.Services.Operations.Start(new OperationOptions { Title = "Long pull" }, async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        await started.Task;

        await _host.CloseAsync(PluginLifetime.Workspace);
        await handle.Completion;

        Assert.Equal(OperationStatus.Cancelled, handle.Status);
    }

    [Fact]
    public async Task SecretsStayOutOfDiagnostics()
    {
        await _host.Services.Configuration.SetAsync("apiToken", "s3cr3t", ConfigTarget.ForConnection("c1"));
        _host.Services.Diagnostics.RegisterSecret("s3cr3t");
        _host.Services.Diagnostics.Write(DiagnosticLevel.Info, "token s3cr3t sent");

        Assert.DoesNotContain("s3cr3t", _host.Services.DiagnosticLog.Lines[0].Message, StringComparison.Ordinal);
        Assert.Equal("s3cr3t", await _host.Services.Configuration.GetSecretAsync("apiToken", ConfigTarget.ForConnection("c1")));
    }
}
