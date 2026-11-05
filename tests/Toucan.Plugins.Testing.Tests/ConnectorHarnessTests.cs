using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing;
using Toucan.Plugins.Testing.Sources;
using Toucan.Sample.Connector;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

/// <summary>The sample connector, written against the public content-source contract (plugin API 1.3).</summary>
public sealed class ConnectorHarnessTests : IAsyncDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions s_web = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly MemoryService _service = new();
    private readonly PluginTestHost _host = new(ConnectorPlugin.Id);

    public ConnectorHarnessTests() => _host.Register(new ConnectorPlugin(_service));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private IContentSourceAdapter Adapter => _host.SourceAdapters[MemorySourceAdapter.Id];

    private async Task<IUnitSourceSession> OpenAsync() =>
        (IUnitSourceSession)await Adapter.OpenSessionAsync(new SourceTestContext(), TestContext.Current.CancellationToken);

    [Fact]
    public void RegistrationAddsOneAdapterAndActivatesNothing()
    {
        Assert.Equal([MemorySourceAdapter.Id], _host.SourceAdapters.Keys);
        Assert.Empty(_host.Commands);
        Assert.Equal(0, _host.ActiveCount);
    }

    [Fact]
    public void ThePluginReferencesNoUiAssembly() =>
        Assert.Empty(CliCompatibility.UiReferences(typeof(ConnectorPlugin).Assembly));

    [Fact]
    public void TheShippedManifestPassesTheRegistrationRules()
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plugin.json")), s_web);
        Assert.NotNull(manifest);
        Assert.Empty(SourceAdapterRules.Check(Adapter, manifest));
    }

    [Fact]
    public async Task ReadsWhatAnotherUserWrote()
    {
        _service.Put("app.save", "de", "Speichern");
        await using var session = await OpenAsync();

        var pages = new List<RemoteUnitPage>();
        await foreach (var page in session.ReadUnitsAsync(new(), TestContext.Current.CancellationToken)) pages.Add(page);

        var page1 = Assert.Single(pages);
        Assert.True(page1.IsComplete);
        Assert.Equal("Speichern", Assert.Single(page1.Units).Value);
    }

    [Fact]
    public async Task WritesAreConditionalAndRepeatedOperationsApplyOnce()
    {
        var unit = _service.Put("app.save", "de", "Speichern");
        await using var session = await OpenAsync();
        var key = new RemoteKey(MemorySourceAdapter.ResourceId, "app.save");
        var id = Guid.NewGuid();

        var first = await session.WriteUnitsAsync(new([new PutValue(id, key, "de", "Sichern", unit.Revision.ToString())]), TestContext.Current.CancellationToken);
        var repeat = await session.WriteUnitsAsync(new([new PutValue(id, key, "de", "Sichern", unit.Revision.ToString())]), TestContext.Current.CancellationToken);
        var stale = await session.WriteUnitsAsync(new([new PutValue(Guid.NewGuid(), key, "de", "Nochmal", unit.Revision.ToString())]), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Applied, first.Results[0].Outcome);
        Assert.Equal(UnitOperationOutcome.AlreadyApplied, repeat.Results[0].Outcome);
        Assert.Equal(first.Results[0].NewRevision, repeat.Results[0].NewRevision);
        Assert.Equal(UnitOperationOutcome.Conflict, stale.Results[0].Outcome);
        Assert.Equal("Sichern", stale.Results[0].CurrentRemote!.Value);
    }
}
