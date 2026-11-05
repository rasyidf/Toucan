using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

/// <summary>The conformance suite passes a correct adapter and catches each deliberate flaw.</summary>
public sealed class SourceConformanceTests
{
    private static readonly PluginManifest s_manifest = new()
    {
        Id = "test.plugin", Name = "Test", Version = "1.0.0", ApiVersion = "1.3", EntryAssembly = "t.dll",
        Capabilities = ["sources"], Network = new NetworkAccess { Hosts = ["source.test"] },
    };

    private static Task<ConformanceReport> RunAsync(FakeAdapterFlaws flaws = FakeAdapterFlaws.None, FakeSourceServer? server = null,
        PluginManifest? manifest = null, SourceCapabilities capabilities = FakeSourceAdapter.AllCapabilities)
    {
        server ??= new FakeSourceServer();
        return SourceAdapterConformance.RunAsync(new FakeSourceAdapter(server, capabilities: capabilities, flaws: flaws), manifest ?? s_manifest,
            new FakeServerFixture(server), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACorrectAdapterPassesEveryApplicableCheck()
    {
        var report = await RunAsync();

        report.ThrowIfFailed();
        Assert.Equal([SourceAdapterConformance.Traffic], report.Results.Where(r => r.Outcome == ConformanceOutcome.Skipped).Select(r => r.Check));
        Assert.Contains("over 3 pages", report[SourceAdapterConformance.ReadPagination].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesThroughAsynchronousJobsPassToo()
    {
        var report = await RunAsync(server: new FakeSourceServer { UseAsyncJobs = true, JobPolls = 3 });
        report.ThrowIfFailed();
    }

    [Theory]
    [InlineData(FakeAdapterFlaws.ClaimsCompleteOnDroppedPage, SourceAdapterConformance.ReadCompleteness, "would infer deletions")]
    [InlineData(FakeAdapterFlaws.ResultsOutOfOrder, SourceAdapterConformance.WriteResults, "keep the batch's order")]
    [InlineData(FakeAdapterFlaws.IgnoresExpectedRevision, SourceAdapterConformance.ConflictDetection, "overwritten")]
    [InlineData(FakeAdapterFlaws.IgnoresCancellation, SourceAdapterConformance.ReadCancellation, "still returned a page")]
    [InlineData(FakeAdapterFlaws.HidesAmbiguousFailures, SourceAdapterConformance.AmbiguousFailure, "would send it again")]
    public async Task EachFlawIsCaught(FakeAdapterFlaws flaw, string check, string expected)
    {
        var report = await RunAsync(flaw);

        Assert.False(report.Passed);
        var result = report[check];
        Assert.Equal(ConformanceOutcome.Failed, result.Outcome);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
        var error = Assert.Throws<SourceConformanceException>(report.ThrowIfFailed);
        Assert.Contains("FAIL " + check, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdapterThatLosesIdempotencyKeysIsReportedButNotFailed()
    {
        var report = await RunAsync(FakeAdapterFlaws.DropsOperationIds);

        Assert.Equal(ConformanceOutcome.Skipped, report[SourceAdapterConformance.WriteIdempotency].Outcome);
        Assert.Equal(ConformanceOutcome.Passed, report[SourceAdapterConformance.CreateIdempotency].Outcome);
    }

    [Fact]
    public async Task ANetworkBlockWithoutTheServiceHostFailsTheDescriptorCheck()
    {
        var report = await RunAsync(manifest: s_manifest with { Network = new NetworkAccess { Hosts = ["elsewhere.test"] } });

        Assert.Equal(ConformanceOutcome.Failed, report[SourceAdapterConformance.Descriptor].Outcome);
        Assert.Contains("'source.test'", report[SourceAdapterConformance.Descriptor].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChecksForUndeclaredCapabilitiesAreSkipped()
    {
        var report = await RunAsync(capabilities: SourceCapabilities.ReadUnits);

        report.ThrowIfFailed();
        Assert.Equal(ConformanceOutcome.Skipped, report[SourceAdapterConformance.WriteResults].Outcome);
        Assert.Equal(ConformanceOutcome.Skipped, report[SourceAdapterConformance.Documents].Outcome);
        Assert.Equal(ConformanceOutcome.Passed, report[SourceAdapterConformance.ReadPagination].Outcome);
    }
}
