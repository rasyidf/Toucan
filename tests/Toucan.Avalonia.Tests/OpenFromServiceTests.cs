using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;
using Avalonia.VisualTree;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>A file-store adapter with project discovery, for the "Open from a service" flow.</summary>
internal sealed class FileServiceAdapter : IContentSourceAdapter, ISupportsProjectDiscovery
{
    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = "test.files", DisplayName = "File service", DefaultBaseUrl = new Uri("https://files.test/"), AuthMethods = [new ApiTokenAuth("token", "Token")],
        Capabilities = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.ProjectDiscovery,
    };

    public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();

    public async IAsyncEnumerable<RemoteProjectSummary> ListProjectsAsync(ISourceAccountContext account, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new("11", "My App", "The app");
        yield return new("12", "Website");
    }
}

/// <summary>Opening a project from a service: the dialog's view model, the recent-list label, and the editor opening what it makes.</summary>
public sealed class OpenFromServiceTests : IDisposable
{
    private readonly TestHost _host = new(configure: s => s.AddSingleton(new RegisteredSourceAdapter("test.plugin", new FileServiceAdapter())));
    private readonly string _root;

    public OpenFromServiceTests() => _root = Path.Combine(_host.Root, "remote");

    public void Dispose() => _host.Dispose();

    private SourceAccount SignedIn() => _host.Services.GetRequiredService<SourceSignInService>().Accounts.Add("test.files", new Uri("https://files.test/"), "token", "me");

    private OpenFromServiceViewModel Vm() => new(_host.Services.GetRequiredService<SourceSignInService>(), _host.Services.GetRequiredService<ISourceAdapterCatalog>(),
        _host.Dialogs, _host.Messages, _root);

    private static async Task SettleAsync(OpenFromServiceViewModel vm)
    {
        for (var i = 0; i < 100 && vm.Projects.Count == 0; i++) await Task.Delay(20);
    }

    [AvaloniaFact]
    public void OnlyFileServicesThatNeedASignInAndCanListProjectsAreOffered()
    {
        var services = Vm().Services.Select(s => s.Descriptor.Id).ToList();
        Assert.Equal(["test.files"], services);
        Assert.DoesNotContain("toucan.sources.local", services); // no sign-in
        Assert.DoesNotContain("toucan.sources.rest", services);  // stores keys, not files
    }

    [AvaloniaFact]
    public async Task ChoosingAnAccountListsItsProjectsAndProposesAFolder()
    {
        SignedIn();
        var vm = Vm();
        Assert.NotNull(vm.SelectedAccount);
        await SettleAsync(vm);

        Assert.Equal(["My App", "Website"], vm.Projects.Select(p => p.Name));
        Assert.Equal("My App", vm.SelectedProject!.Name);
        Assert.Equal(Path.Combine(_root, "file-service", "My-App"), vm.Folder);
        Assert.True(vm.CanOpen);
    }

    [AvaloniaFact]
    public async Task ADefaultFolderFollowsTheProjectUntilTheUserChoosesOne()
    {
        SignedIn();
        var vm = Vm();
        await SettleAsync(vm);

        vm.SelectedProject = vm.Projects[1];
        Assert.Equal(Path.Combine(_root, "file-service", "Website"), vm.Folder);

        vm.FolderChangedByUser("/chosen/place");
        vm.SelectedProject = vm.Projects[0];
        Assert.Equal("/chosen/place", vm.Folder);
    }

    [AvaloniaFact]
    public async Task BrowseUsesTheFolderPicker()
    {
        SignedIn();
        var vm = Vm();
        await SettleAsync(vm);
        _host.Dialogs.Folders.Enqueue("/picked");

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal("/picked", vm.Folder);
    }

    [AvaloniaFact]
    public async Task OpeningHandsTheChoicesToTheHostAndClosesOnSuccess()
    {
        var account = SignedIn();
        var vm = Vm();
        await SettleAsync(vm);
        (string Adapter, string Account, string Project, string Folder)? seen = null;
        vm.Create = (adapter, acc, project, folder, _, _) =>
        {
            seen = (adapter.Descriptor.Id, acc.Id, project.Name, folder);
            return Task.FromResult(new RemoteProjectResult(folder, "file-service:My App", new SourceConnectionSettings { Id = "file-service" }, 3));
        };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal(("test.files", account.Id, "My App", vm.Folder), seen);
        Assert.True(closed);
        Assert.Equal("file-service:My App", vm.Result!.Label);
    }

    [AvaloniaFact]
    public async Task AFailureStaysInTheDialogWithTheReason()
    {
        SignedIn();
        var vm = Vm();
        await SettleAsync(vm);
        vm.Create = (_, _, _, _, _, _) => throw new InvalidOperationException("The folder is not empty.");
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.OpenCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal("The folder is not empty.", vm.Problem);
        Assert.True(vm.HasProblem);
        Assert.Null(vm.Result);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public void WithoutAnAccountThereIsNothingToOpen()
    {
        var vm = Vm();
        Assert.Null(vm.SelectedAccount);
        Assert.False(vm.CanOpen);
    }

    [AvaloniaFact]
    public async Task TheDialogRenders()
    {
        SignedIn();
        var vm = Vm();
        await SettleAsync(vm);
        var dialog = new OpenFromServiceDialog(vm);
        dialog.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(dialog.GetVisualDescendants().OfType<global::Avalonia.Controls.TextBlock>(), t => t.Text == "Open from a service");
        dialog.Close();
    }

    // ───────────── recent list ─────────────

    [Fact]
    public void RecentProjectsShowTheLabelAndKeepItAcrossReopens()
    {
        var file = Path.Combine(_host.Root, "recent-labels.json");
        var folder = Directory.CreateDirectory(Path.Combine(_host.Root, "crowdin-demo")).FullName;
        var recents = new RecentProjectService(file);
        recents.Add(folder);
        Assert.Equal("crowdin-demo", recents.LoadRecent().Single().DisplayName);

        recents.SetLabel(folder, "crowdin:toucan-connector-test");
        recents.Add(folder); // opening it again must not lose the label

        Assert.Equal("crowdin:toucan-connector-test", new RecentProjectService(file).LoadRecent().Single().DisplayName);
        recents.SetLabel(folder, null);
        Assert.Equal("crowdin-demo", recents.LoadRecent().Single().DisplayName);
    }

    // ───────────── the editor opens what the bootstrap makes ─────────────

    private sealed class FileSession(Dictionary<string, string> files) : IDocumentSourceSession
    {
        public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) => Task.FromResult(SourceTestResult.Ok());

        public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SourceProjectInfo("11", "My App", "en", [new("en", "English", true), new("fr", "French")], [], SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments));

        public async IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new([.. files.Select(f => new RemoteDocumentInfo(new(f.Key), "v-" + f.Value.Length))], null, true);
        }

        public Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteDocumentContent(document, System.Text.Encoding.UTF8.GetBytes(files[document.Path]), "v-" + files[document.Path].Length));

        public Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task AProjectMadeFromAServiceOpensInTheEditorWithItsConnectionAndLanguages()
    {
        var files = new Dictionary<string, string>
        {
            ["locales/en/common.json"] = """{"title":"Welcome"}""",
            ["locales/fr/common.json"] = """{"title":"Bienvenue"}""",
        };
        var projects = _host.Services.GetRequiredService<IProjectService>();
        var bootstrap = new RemoteProjectBootstrap((_, _, _) => Task.FromResult<IContentSourceSession>(new FileSession(files)), projects.DetectFramework);
        var adapter = _host.Services.GetRequiredService<ISourceAdapterCatalog>().Find("test.files")!;

        var made = await bootstrap.CreateAsync(adapter, null, new RemoteProjectSummary("11", "My App"), Path.Combine(_host.Root, "from-service"));

        Assert.Equal("file-service:My App", made.Label);
        var vm = _host.CreateViewModel();
        await vm.OpenProjectAsync(made.Folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the panel reloads when the project opens

        Assert.Contains(vm.AllTranslation, t => t.Language == "fr" && t.Value == "Bienvenue");
        Assert.Contains(vm.AllTranslation, t => t.Language == "en" && t.Value == "Welcome");
        Assert.Equal("My App", vm.ProjectName);
        var connection = Assert.Single(vm.Sources!.Connections);
        Assert.Equal(("file-service", SourceRoles.Primary), (connection.Id, connection.Settings.Role));
        Assert.Equal("fr", connection.Settings.Languages["fr"]);
    }

    [AvaloniaFact]
    public async Task TheStartScreenOffersOpenFromAServiceWhenAConnectorIsInstalled()
    {
        var vm = _host.CreateViewModel();
        Assert.True(vm.HasSources);
        _host.Dialogs.OnOpenFromService = _ => null;
        await vm.OpenFromServiceCommand.ExecuteAsync(null);
        Assert.Single(_host.Dialogs.OpenFromServiceShown);
        Assert.Equal(["test.files"], _host.Dialogs.OpenFromServiceShown[0].Services.Select(s => s.Descriptor.Id));
    }
}

/// <summary>How a language reads on a row: its own name, then the code, on one line.</summary>
public sealed class LanguageLabelTests
{
    [Theory]
    [InlineData("de-DE", "Deutsch (de)")]
    [InlineData("de", "Deutsch (de)")]
    [InlineData("fr-FR", "Français (fr)")]
    [InlineData("en-US", "English (en)")]
    [InlineData("de-AT", "Deutsch (de-AT)")]
    [InlineData("en-GB", "English (en-GB)")]
    [InlineData("pt-PT", "Português (pt-PT)")]
    [InlineData("de_DE", "Deutsch (de)")]
    public void ShowsTheLanguagesOwnNameAndTheCode(string code, string expected) =>
        Assert.Equal(expected, Toucan.Avalonia.Converters.LanguageLabelConverter.Label(code));

    [Theory]
    [InlineData("")]
    [InlineData("xx-custom-tag")]
    public void CustomTagsStayAsTheyAre(string code) =>
        Assert.Equal(code, Toucan.Avalonia.Converters.LanguageLabelConverter.Label(code));
}
