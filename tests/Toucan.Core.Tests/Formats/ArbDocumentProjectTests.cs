using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Flutter projects open through document saving (V1-11): languages of one message group as one entry, a save changes only what
/// was edited, comments stay in <c>@key.description</c>, and projects saved by the full-rewrite path keep their approvals.
/// </summary>
public sealed class ArbDocumentProjectTests : IDisposable
{
    private const string English = """
        {
          "@@locale": "en",
          "greeting": "Hello {name}",
          "@greeting": {"description": "Home title", "placeholders": {"name": {"type": "String", "x-keep": 1}}},
          "bye": "Goodbye"
        }

        """;
    private const string French = """
        {
          "@@locale": "fr",
          "greeting": "Bonjour {name}",
          "bye": "Au revoir"
        }

        """;

    private readonly string _root = Directory.CreateTempSubdirectory("toucan-arb-document-").FullName;
    private readonly ServiceProvider _services;

    public ArbDocumentProjectTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Write("pubspec.yaml", "name: demo\n");
        Write("lib/l10n/app_en.arb", English);
        Write("lib/l10n/app_fr.arb", French);
    }

    public void Dispose()
    {
        _services.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    private (ProjectLoadResult Loaded, WorkspaceService Workspace) Open()
    {
        var loaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        Assert.NotNull(loaded.Source);
        var workspace = _services.GetRequiredService<WorkspaceService>();
        workspace.Open(loaded.Settings, loaded.Source);
        return (loaded, workspace);
    }

    private Task Save(WorkspaceService workspace) => _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(workspace.Source!);

    private static async Task Apply(WorkspaceService workspace, Func<IdentifiedWorkspaceSnapshot, EntryOperation[]> operations)
    {
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        Assert.True((await workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision, operations(snapshot)))).Applied);
    }

    private static IdentifiedUnitSnapshot Unit(IdentifiedWorkspaceSnapshot s, string key, string language) =>
        s.Units.Single(u => u.Key == key && u.Language == language);

    [Fact]
    public void APlainFlutterFolderOpensThroughDocumentSavingWithLanguagesGrouped()
    {
        var (loaded, _) = Open();
        Assert.Equal("flutter-arb", loaded.Settings.Framework);
        Assert.Equal(2, loaded.Source!.Units.Select(u => u.EntryId).Distinct().Count());
        var greeting = loaded.Translations.Single(t => t.Namespace == "greeting" && t.Language == "en");
        Assert.Equal("Home title", greeting.Comment);
        Assert.Contains("x-keep", greeting.FormatData![ArbFormat.Placeholders]);
    }

    [Fact]
    public async Task SavingChangesOnlyTheEditedTokensAndKeepsCommentsInTheFile()
    {
        var (_, workspace) = Open();
        await Apply(workspace, s => [new SetEntryValue(Unit(s, "bye", "fr").EntryId, "fr", "Salut"),
            new SetEntryComment(Unit(s, "greeting", "en").EntryId, "en", "Title on the home screen")]);
        await Save(workspace);

        Assert.Equal(French.Replace("Au revoir", "Salut"), Read("lib/l10n/app_fr.arb"));
        Assert.Equal(English.Replace("Home title", "Title on the home screen"), Read("lib/l10n/app_en.arb"));
        // The comment lives in the ARB file, not in a sidecar.
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(_root, ".toucan"), "*", SearchOption.AllDirectories),
            f => File.ReadAllText(f).Contains("Title on the home screen", StringComparison.Ordinal));

        var (reopened, _) = Open();
        Assert.Equal("Title on the home screen", reopened.Translations.Single(t => t.Namespace == "greeting" && t.Language == "en").Comment);
    }

    [Fact]
    public async Task ADescriptionEditedOutsideToucanWinsOverAnOlderSidecar()
    {
        var (_, workspace) = Open();
        await Apply(workspace, s => [new SetEntryReview(Unit(s, "bye", "fr").EntryId, "fr", ReviewState.Approved)]);
        await Save(workspace);
        Write("lib/l10n/app_en.arb", English.Replace("Home title", "Edited in the IDE"));

        var (reopened, _) = Open();
        Assert.Equal("Edited in the IDE", reopened.Translations.Single(t => t.Namespace == "greeting" && t.Language == "en").Comment);
        Assert.True(reopened.Translations.Single(t => t.Namespace == "bye" && t.Language == "fr").IsApproved);
    }

    [Fact]
    public async Task AProjectSavedByTheFullRewritePathKeepsItsApprovalsAndUsesTheFilesComments()
    {
        // Written by an earlier Toucan: no framework, approvals and a stale comment in toucan.tproj.
        new ProjectSettings
        {
            ProjectPath = _root, SaveFormat = FormatIds.Arb, PrimaryLanguage = "en", Languages = ["en", "fr"],
            TranslationStates =
            [
                new() { Language = "fr", Key = "bye", Approved = true },
                new() { Language = "en", Key = "greeting", Comment = "Old comment from the manifest" },
            ],
        }.Save();

        var (loaded, workspace) = Open();
        Assert.Equal("flutter-arb", loaded.Settings.Framework);
        Assert.True(loaded.Translations.Single(t => t.Namespace == "bye" && t.Language == "fr").IsApproved);
        Assert.Equal("Home title", loaded.Translations.Single(t => t.Namespace == "greeting" && t.Language == "en").Comment);

        await Apply(workspace, s => [new SetEntryValue(Unit(s, "greeting", "fr").EntryId, "fr", "Salut {name}")]);
        await Save(workspace);
        Assert.Equal(French.Replace("Bonjour {name}", "Salut {name}"), Read("lib/l10n/app_fr.arb"));
        Assert.Equal(English, Read("lib/l10n/app_en.arb"));

        var (reopened, _) = Open();
        Assert.True(reopened.Translations.Single(t => t.Namespace == "bye" && t.Language == "fr").IsApproved);
    }

    [Fact]
    public void ANewLanguageFileFollowsTheProjectsNaming()
    {
        var (_, workspace) = Open();
        var path = _services.GetRequiredService<WorkspaceDocumentRouter>().NewEntryPath(workspace.Source!, "de");
        Assert.Equal(Path.Combine(_root, "lib", "l10n", "app_de.arb"), path);
    }

    [Fact]
    public async Task RecoveryCountsOnlyRealChangesAndReplaysAPlaceholderEdit()
    {
        var (_, workspace) = Open();
        var untouched = workspace.CaptureRecovery(workspace.Source!);
        Assert.Equal(0, untouched.ChangeCount);

        var units = untouched.Units.Select(u => u.KeyPath.Segments[0] == "greeting" && u.Language == "en"
            ? u with { FormatData = new Dictionary<string, string>(u.FormatData!) { [ArbFormat.Placeholders] = """{"name":{"type":"String","example":"Grace"}}""" } }
            : u).ToArray();
        var draft = new WorkspaceRecoveryDraft(untouched.Root, untouched.FrameworkId, untouched.FormatId, untouched.Baseline, units, untouched.IdentityCheckpoint);
        Assert.Equal(1, draft.ChangeCount);

        Assert.Equal(1, workspace.ApplyRecovery(draft).Applied);
        await Save(workspace);
        var saved = Read("lib/l10n/app_en.arb");
        Assert.Contains("Grace", saved);
        Assert.Contains("Home title", saved);
        Assert.DoesNotContain("x-keep", saved);
        Assert.Equal(French, Read("lib/l10n/app_fr.arb"));
    }
}
