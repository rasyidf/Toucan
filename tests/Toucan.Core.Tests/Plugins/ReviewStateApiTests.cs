using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

/// <summary>Plugin API 1.3 exposes needs-review; plugins built for older APIs keep seeing it as Draft.</summary>
public sealed class ReviewStateApiTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly ITranslationManagementService _store;
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-review-api-").FullName;

    public ReviewStateApiTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _store = _services.GetRequiredService<ITranslationManagementService>();
        _store.Initialize([
            new() { Namespace = "hello", Language = "en", Value = "Hello" },
            new() { Namespace = "hello", Language = "fr", Value = "Bonjour", NeedsReview = true },
            new() { Namespace = "bye", Language = "fr", Value = "Au revoir", IsApproved = true },
        ]);
        _workspace.Open(new ProjectSettings { ProjectPath = "/workspace", PrimaryLanguage = "en", Languages = ["en", "fr"] });
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
        TempFolder.TryDelete(_root);
    }

    private IWorkspaceSubscriptionApi Api(string apiVersion)
    {
        var host = new PluginHostServices();
        host.Broker.Attach(new ServiceCollection().AddSingleton<IWorkspaceBackend>(_workspace).BuildServiceProvider());
        return (IWorkspaceSubscriptionApi)new PluginServices("acme.review", host, _root, Version.Parse(apiVersion)).Workspace;
    }

    [Fact]
    public async Task CurrentApiSeesNeedsReviewInEverySnapshot()
    {
        var api = Api("1.3");
        Assert.Equal(ReviewState.NeedsReview, (await api.SnapshotAsync())!.Find("hello", "fr")!.Review);
        Assert.Equal(ReviewState.Approved, (await api.SnapshotAsync())!.Find("bye", "fr")!.Review);
        var entries = (await api.SnapshotEntriesAsync())!;
        Assert.Equal(ReviewState.NeedsReview, entries.Units.Single(u => u.Key == "hello" && u.Language == "fr").Review);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.2")]
    public async Task OlderApisSeeNeedsReviewAsDraft(string apiVersion)
    {
        var api = Api(apiVersion);
        Assert.Equal(ReviewState.Draft, (await api.SnapshotAsync())!.Find("hello", "fr")!.Review);
        Assert.Equal(ReviewState.Approved, (await api.SnapshotAsync())!.Find("bye", "fr")!.Review);
        var entries = (await api.SnapshotEntriesAsync())!;
        Assert.DoesNotContain(entries.Units, u => u.Review == ReviewState.NeedsReview);
    }

    [Fact]
    public async Task OlderApiSubscriptionsNeverCarryNeedsReview()
    {
        var api = Api("1.2");
        await using var subscription = (await api.SubscribeAsync())!;
        Assert.DoesNotContain(subscription.InitialSnapshot.Units, u => u.Review == ReviewState.NeedsReview);

        var bye = subscription.InitialSnapshot.Units.Single(u => u.Key == "bye" && u.Language == "fr");
        var result = await Api("1.3").ApplyEntriesAsync(new(subscription.InitialSnapshot.SessionId, subscription.InitialSnapshot.Revision,
            [new SetEntryReview(bye.EntryId, "fr", ReviewState.NeedsReview)]));
        Assert.True(result.Applied);

        await using var enumerator = subscription.Changes.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(ReviewState.Draft, Assert.Single(enumerator.Current.Units).Unit!.Review);
    }

    [Fact]
    public async Task SettingNeedsReviewWithdrawsApprovalAndApprovingClearsIt()
    {
        var api = Api("1.3");
        var snapshot = (await api.SnapshotEntriesAsync())!;
        var bye = snapshot.Units.Single(u => u.Key == "bye" && u.Language == "fr");

        Assert.True((await api.ApplyEntriesAsync(new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryReview(bye.EntryId, "fr", ReviewState.NeedsReview)]))).Applied);
        var item = _store.Translations.Single(i => i.Namespace == "bye" && i.Language == "fr");
        Assert.False(item.IsApproved);
        Assert.True(item.NeedsReview);
        Assert.Equal(ReviewStatus.NeedsReview, item.Review);

        Assert.True((await api.ApplyAsync(new WorkspaceEdit().SetReview("bye", "fr", ReviewState.Approved))).Applied);
        Assert.Equal(ReviewState.Approved, (await api.SnapshotAsync())!.Find("bye", "fr")!.Review);
        Assert.False(_store.Translations.Single(i => i.Namespace == "bye" && i.Language == "fr").NeedsReview);

        Assert.True((await api.ApplyAsync(new WorkspaceEdit().SetReview("bye", "fr", ReviewState.Draft))).Applied);
        var draft = _store.Translations.Single(i => i.Namespace == "bye" && i.Language == "fr");
        Assert.Equal(ReviewStatus.Draft, draft.Review);
    }

    [Fact]
    public async Task NeedsReviewSurvivesUndo()
    {
        var api = Api("1.3");
        var snapshot = (await api.SnapshotEntriesAsync())!;
        var hello = snapshot.Units.Single(u => u.Key == "hello" && u.Language == "fr");
        Assert.True((await api.ApplyEntriesAsync(new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryReview(hello.EntryId, "fr", ReviewState.Draft)]))).Applied);
        Assert.Equal(ReviewState.Draft, (await api.SnapshotAsync())!.Find("hello", "fr")!.Review);

        Assert.True(_workspace.Undo());
        Assert.Equal(ReviewState.NeedsReview, (await api.SnapshotAsync())!.Find("hello", "fr")!.Review);
        Assert.True(_store.Translations.Single(i => i.Namespace == "hello" && i.Language == "fr").NeedsReview);
    }
}
