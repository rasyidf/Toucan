using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>
/// v0.26 gate for document-persistence projects: review state and the source it was given for survive
/// save and reopen, and a source changed between sessions withdraws the approval.
/// </summary>
public sealed class DocumentReviewPersistenceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-document-review-").FullName;
    private readonly ServiceProvider _services;

    public DocumentReviewPersistenceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void CreateProject()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        new ProjectSettings { ProjectPath = _root, Framework = "i18next", SaveFormat = FormatIds.Namespaced,
            PrimaryLanguage = "en", Languages = ["en", "fr"] }.Save();
    }

    private WorkspaceService Open()
    {
        var loaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        Assert.NotNull(loaded.Source);
        var workspace = _services.GetRequiredService<WorkspaceService>();
        workspace.Open(loaded.Settings, loaded.Source);
        return workspace;
    }

    private Task Save(WorkspaceService workspace) =>
        _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(workspace.Source!);

    private static async Task<IdentifiedUnitSnapshot> French(WorkspaceService workspace) =>
        (await workspace.SnapshotEntriesAsync())!.Units.Single(u => u.Language == "fr");

    private async Task<WorkspaceService> ApproveFrenchAndSave()
    {
        CreateProject();
        var workspace = Open();
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        var fr = snapshot.Units.Single(u => u.Language == "fr");
        await workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryReview(fr.EntryId, "fr", ReviewState.Approved)]));
        await Save(workspace);
        return workspace;
    }

    [Fact]
    public async Task ApprovalSurvivesSaveAndReopen()
    {
        await ApproveFrenchAndSave();
        var reopened = Open();
        Assert.Equal(ReviewState.Approved, (await French(reopened)).Review);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public async Task SourceChangedBetweenSessionsWithdrawsTheApproval()
    {
        await ApproveFrenchAndSave();
        Write("locales/en/common.json", "{\"title\":\"Hello there\"}");

        var reopened = Open();
        Assert.Equal(ReviewState.NeedsReview, (await French(reopened)).Review);
    }

    [Fact]
    public async Task NeedsReviewSurvivesSaveAndReopen()
    {
        await ApproveFrenchAndSave();
        Write("locales/en/common.json", "{\"title\":\"Hello there\"}");
        var withdrawn = Open();
        await Save(withdrawn);

        var reopened = Open();
        Assert.Equal(ReviewState.NeedsReview, (await French(reopened)).Review);
    }

    [Fact]
    public async Task SidecarsWithoutReviewStateKeepTheirBytes()
    {
        CreateProject();
        var workspace = Open();
        await workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "fr", "Note"));
        await Save(workspace);
        var sidecar = Directory.GetFiles(Path.Combine(_root, ".toucan", "entries")).Single();
        var text = File.ReadAllText(sidecar);
        Assert.DoesNotContain("NeedsReview", text);
        Assert.DoesNotContain("ReviewedSourceHash", text);
    }

    public void Dispose() { _services.Dispose(); Directory.Delete(_root, recursive: true); }
}
