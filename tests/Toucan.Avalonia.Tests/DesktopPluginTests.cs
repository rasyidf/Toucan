using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Panels;
using Toucan.Core.Commands;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Desktop;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class DesktopPluginTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-desktop-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // a loaded plugin DLL stays locked on Windows
    }

    /// <summary>Installs the real sample plugin, trusts it, and starts the application services on those files.</summary>
    private TestHost HostWithSample()
    {
        var dir = Path.Combine(_root, "plugins", "sample.tsv");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SamplePlugin")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        new FilePluginPolicyStore(Path.Combine(_root, "plugin-policy.json")).Trust("sample.tsv", PluginHasher.Compute(dir));
        return new TestHost(_root);
    }

    private static (PluginWorkspaceContext Workspace, DesktopHost Host) Context(TestHost host, DesktopContributions contributions)
    {
        var workspace = new PluginWorkspaceContext(host.CreateViewModel(), host.Services.GetRequiredService<ICommandRegistry>());
        return (workspace, new DesktopHost(contributions, workspace));
    }

    // --- the real sample ------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheSamplesDesktopPartLoadsAndRegistersEverythingItContributes()
    {
        using var host = HostWithSample();
        var contributions = new DesktopContributions();
        var panels = new SidePanelRegistry();
        var (workspace, desktop) = Context(host, contributions);

        var results = DesktopPluginLoader.LoadAll(host.Services.GetRequiredService<IPluginCatalog>(), contributions, panels, workspace, desktop,
            host.Services.GetRequiredService<ICommandRegistry>(), services: host.Services.GetServices<PluginServicesRegistration>());

        Assert.Equal(DesktopLoadStatus.Loaded, Assert.Single(results).Status);
        Assert.True(contributions.HasPanel("sample.tsv.panel"));
        Assert.Contains(panels.RightSlotPanels, p => p.Id == "sample.tsv.panel");
        Assert.Single(contributions.InspectorSections);
        Assert.Single(contributions.SettingsSections);
        Assert.Equal("sample.tsv.stamp", Assert.Single(contributions.KeyActions).Contribution.CommandId);
        Assert.NotNull(contributions.FindDialog("sample.tsv.about"));

        var vm = host.CreateViewModel();
        Assert.IsAssignableFrom<Control>(contributions.CreatePanel("sample.tsv.panel", vm));
        // The panel's toolbar button runs a command from the registry and so follows its state.
        var action = Assert.Single(contributions.PanelActions("sample.tsv.panel", vm));
        Assert.False(action.Command.CanExecute(null)); // no project is open
    }

    [AvaloniaFact]
    public void ThePanelContentDoesNotGetTheMainViewModel()
    {
        using var host = HostWithSample();
        var contributions = new DesktopContributions();
        var (workspace, desktop) = Context(host, contributions);
        DesktopPluginLoader.LoadAll(host.Services.GetRequiredService<IPluginCatalog>(), contributions, new SidePanelRegistry(), workspace, desktop,
            host.Services.GetRequiredService<ICommandRegistry>(), services: host.Services.GetServices<PluginServicesRegistration>());

        var panel = contributions.CreatePanel("sample.tsv.panel", host.CreateViewModel())!;

        Assert.Null(panel.DataContext);
    }

    [AvaloniaFact]
    public void KeyActionsFromPluginsAreCollectedForMenusAndTheInspector()
    {
        using var host = HostWithSample();
        var contributions = new DesktopContributions();
        var (workspace, desktop) = Context(host, contributions);
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        DesktopPluginLoader.LoadAll(host.Services.GetRequiredService<IPluginCatalog>(), contributions, new SidePanelRegistry(), workspace, desktop, commands,
            services: host.Services.GetServices<PluginServicesRegistration>());

        var actions = KeyActions.Collect(commands, contributions);

        var action = Assert.Single(actions); // the command is also registered, but listed once
        Assert.Equal("Stamp this key", action.Title);
        Assert.Equal("sample.tsv.stamp", action.CommandId);
    }

    [AvaloniaFact]
    public async Task ThePanelWorkspaceFollowsTheOpenProject()
    {
        using var host = HostWithSample();
        var vm = host.CreateViewModel();
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        using var workspace = new PluginWorkspaceContext(vm, commands);
        var changes = 0;
        workspace.Changed += (_, _) => changes++;
        Assert.False(workspace.HasWorkspace);
        Assert.Null(workspace.ProjectPath);

        var folder = host.CreateJsonProject("p", ("en", "{\"a\":\"A\"}"), ("fr", "{\"a\":\"B\"}"));
        await vm.OpenProjectAsync(folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(workspace.HasWorkspace);
        Assert.Equal(folder, workspace.ProjectPath);
        Assert.NotNull(workspace.WorkspaceId);
        Assert.True(changes > 0);
    }

    // --- failure handling -------------------------------------------------------------------------------------------

    private sealed class FakeDesktopPlugin(Action<IDesktopPluginContext> init) : IToucanDesktopPlugin
    {
        public void InitializeDesktop(IDesktopPluginContext context) => init(context);
    }

    private static Control Content(IPluginWorkspace _) => new TextBlock { Text = "x" };

    private DesktopLoadResult Run(string pluginId, Action<IDesktopPluginContext> init, DesktopContributions contributions, SidePanelRegistry panels)
    {
        using var host = new TestHost();
        var (workspace, desktop) = Context(host, contributions);
        return DesktopPluginLoader.Initialize(pluginId, new FakeDesktopPlugin(init), contributions, panels, workspace, desktop,
            host.Services.GetRequiredService<ICommandRegistry>(), NullLogger.Instance);
    }

    [AvaloniaFact]
    public void IdsMustStartWithThePluginIdAndNothingIsRegisteredOtherwise()
    {
        var contributions = new DesktopContributions();
        var panels = new SidePanelRegistry();

        var result = Run("acme.sync", c =>
        {
            c.AddSidePanel(new SidePanelContribution { Id = "acme.sync.ok", Title = "Ok", CreateContent = Content });
            c.AddSidePanel(new SidePanelContribution { Id = "elsewhere.panel", Title = "Bad", CreateContent = Content });
        }, contributions, panels);

        Assert.Equal(DesktopLoadStatus.Failed, result.Status);
        Assert.Contains("must start with the plugin ID", result.Error);
        Assert.False(contributions.HasPanel("acme.sync.ok")); // all or nothing
        Assert.Empty(panels.RightSlotPanels);
    }

    [AvaloniaFact]
    public void DuplicateIdsAndMissingTitlesAreRejected()
    {
        var duplicate = Run("acme.sync", c =>
        {
            c.AddSidePanel(new SidePanelContribution { Id = "acme.sync.a", Title = "A", CreateContent = Content });
            c.AddInspectorSection(new InspectorSectionContribution { Id = "acme.sync.a", Title = "A", CreateContent = Content });
        }, new DesktopContributions(), new SidePanelRegistry());
        var untitled = Run("acme.sync", c => c.AddSidePanel(new SidePanelContribution { Id = "acme.sync.a", Title = " ", CreateContent = Content }),
            new DesktopContributions(), new SidePanelRegistry());

        Assert.Contains("twice", duplicate.Error);
        Assert.Contains("needs a title", untitled.Error);
    }

    [AvaloniaFact]
    public void AnEntryPointThatThrowsFailsOnlyThatPlugin()
    {
        var contributions = new DesktopContributions();

        var result = Run("acme.sync", _ => throw new InvalidOperationException("no display"), contributions, new SidePanelRegistry());

        Assert.Equal(DesktopLoadStatus.Failed, result.Status);
        Assert.Contains("no display", result.Error);
        Assert.False(contributions.HasPanel("acme.sync.a"));
    }

    [AvaloniaFact]
    public void ADesktopPartBuiltForANewerContractIsReportedNotLoaded()
    {
        using var host = new TestHost();
        var contributions = new DesktopContributions();
        var (workspace, desktop) = Context(host, contributions);
        var manifest = new PluginManifest
        {
            Id = "acme.sync", Name = "Acme", Version = "1.0.0", ApiVersion = "1.1", EntryAssembly = "a.dll", Capabilities = ["desktop"],
            Desktop = new DesktopEntry { EntryAssembly = "a.desktop.dll", ContractVersion = "2.0" },
        };
        var catalog = new FakeCatalog(new PluginLoadResult(_root, PluginStatus.Loaded, manifest));

        var result = Assert.Single(DesktopPluginLoader.LoadAll(catalog, contributions, new SidePanelRegistry(), workspace, desktop,
            host.Services.GetRequiredService<ICommandRegistry>()));

        Assert.Equal(DesktopLoadStatus.Incompatible, result.Status);
        Assert.Contains("desktop contract 2.0", result.Error);
        Assert.Equal(DesktopLoadStatus.Incompatible, Assert.Single(contributions.LoadResults).Status);
    }

    [Fact]
    public void PluginsWithoutADesktopPartAreIgnored()
    {
        using var host = new TestHost();
        var contributions = new DesktopContributions();
        var manifest = new PluginManifest { Id = "acme.sync", Name = "Acme", Version = "1.0.0", ApiVersion = "1.1", EntryAssembly = "a.dll" };
        var catalog = new FakeCatalog(new PluginLoadResult(_root, PluginStatus.Loaded, manifest));
        var (workspace, desktop) = Context(host, contributions);

        Assert.Empty(DesktopPluginLoader.LoadAll(catalog, contributions, new SidePanelRegistry(), workspace, desktop, host.Services.GetRequiredService<ICommandRegistry>()));
    }

    private sealed class FakeCatalog(params PluginLoadResult[] plugins) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
    }

    // --- built-in panels use the same registry --------------------------------------------------------------------

    [AvaloniaFact]
    public void EveryBuiltInPanelIsRegisteredLikeAPluginPanel()
    {
        App.RegisterSidePanels();
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var registry = DesktopContributions.Instance;

        foreach (var id in new[] { "explorer", "search", "issues", "source-code", "languages", "inspector", "machine-translation", "translation-memory" })
        {
            Assert.True(registry.HasPanel(id), id);
            var panel = registry.CreatePanel(id, vm);
            Assert.NotNull(panel);
            Assert.Same(vm, panel.DataContext);
        }
        Assert.IsType<InspectorPanel>(registry.CreatePanel("inspector", vm));
        Assert.NotEmpty(registry.PanelActions("explorer", vm));
    }

    [Fact]
    public void DesktopContractCompatibilityMirrorsThePluginApiRule()
    {
        Assert.True(DesktopContract.IsCompatible(new Version(1, 0)));
        Assert.False(DesktopContract.IsCompatible(new Version(1, 1)));
        Assert.False(DesktopContract.IsCompatible(new Version(2, 0)));
        Assert.False(DesktopContract.IsCompatible(new Version(0, 9)));
    }
}
