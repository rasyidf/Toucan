using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Core.Commands;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>
/// The v0.23 completion gate for the extension platform: the sample connector built <em>outside</em> this repository
/// (toucan-plugins, built against the published SDK packages only) loads on the desktop and in the CLI, its commands
/// appear with reassignable shortcuts in the palette and a menu, its desktop panel registers, and its command becomes
/// unavailable when the workspace closes.
///
/// Point <c>TOUCAN_EXTERNAL_CONNECTOR_DIST</c> at the connector's built plugin folder
/// (<c>toucan-plugins/dist/toucan-plugins.rest-connector</c>) to run it; the test skips otherwise so a normal CI run,
/// which does not have the external repository, stays green. This is the release acceptance check, run when cutting
/// the release, not a per-commit regression test.
/// </summary>
public sealed class ExternalConnectorGateTests : IDisposable
{
    private const string PluginId = "toucan-plugins.rest-connector";
    private const string Pull = "toucan-plugins.rest-connector.pull";
    private const string Push = "toucan-plugins.rest-connector.push";
    private const string Panel = "toucan-plugins.rest-connector.panel";

    private readonly string? _dist = Environment.GetEnvironmentVariable("TOUCAN_EXTERNAL_CONNECTOR_DIST");
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-ext-connector-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Copies the externally built plugin folder into a temp plugin root and trusts it, then starts the real services.</summary>
    private TestHost HostWithExternalConnector()
    {
        Assert.True(Directory.Exists(_dist), $"External connector dist not found at '{_dist}'. Run ./build.sh in toucan-plugins.");
        var dir = Path.Combine(_root, "plugins", PluginId);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(_dist!))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        new FilePluginPolicyStore(Path.Combine(_root, "plugin-policy.json")).Trust(PluginId, PluginHasher.Compute(dir));
        return new TestHost(_root);
    }

    private static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOUCAN_EXTERNAL_CONNECTOR_DIST"));

    [AvaloniaFact]
    public void TheExternallyBuiltConnectorLoadsAndMeetsTheGate()
    {
        Assert.SkipUnless(Enabled, "Set TOUCAN_EXTERNAL_CONNECTOR_DIST to the built connector folder to run the v0.23 gate check.");
        using var host = HostWithExternalConnector();

        // 1. The host (version 0.23.0) loads the plugin: minHostVersion 0.23.0 is satisfied and nothing is rejected.
        var catalog = host.Services.GetRequiredService<IPluginCatalog>();
        var plugin = Assert.Single(catalog.Plugins, p => p.Manifest?.Id == PluginId);
        Assert.Equal(PluginStatus.Loaded, plugin.Status);

        // 2. Both commands are registered in the shared registry with their categories and placements.
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var pull = commands.Find(Pull);
        var push = commands.Find(Push);
        Assert.NotNull(pull);
        Assert.NotNull(push);
        Assert.True(pull!.Definition.Placements.HasFlag(CommandPlacement.CommandPalette), "Pull should appear in the command palette.");
        Assert.True(pull.Definition.Placements.HasFlag(CommandPlacement.Menu), "Pull should appear in a menu.");

        // 3. The default shortcut is in effect and can be reassigned by the user.
        Assert.Equal("Mod+Alt+L", commands.GetShortcut(Pull));
        Assert.True(commands.TrySetShortcut(Pull, "Mod+Alt+K", out var conflicts), $"Reassignment should succeed; conflicts: {conflicts.Count}");
        Assert.Equal("Mod+Alt+K", commands.GetShortcut(Pull));
        commands.TrySetShortcut(Pull, null, out _); // restore the default

        // 4. The desktop part loads and registers its Server panel with Pull/Push toolbar actions.
        var contributions = new DesktopContributions();
        var panels = new SidePanelRegistry();
        var workspace = new PluginWorkspaceContext(host.CreateViewModel(), commands);
        var desktop = new DesktopHost(contributions, workspace);
        var results = DesktopPluginLoader.LoadAll(catalog, contributions, panels, workspace, desktop, commands,
            services: host.Services.GetServices<PluginServicesRegistration>());

        Assert.Equal(DesktopLoadStatus.Loaded, Assert.Single(results).Status);
        Assert.True(contributions.HasPanel(Panel));
        Assert.Contains(panels.RightSlotPanels, p => p.Id == Panel);
        var vm = host.CreateViewModel();
        Assert.IsAssignableFrom<Control>(contributions.CreatePanel(Panel, vm));
        var actions = contributions.PanelActions(Panel, vm);
        Assert.Equal(2, actions.Count); // Pull and Push buttons, each running a registry command

        // 5. With no project open the command is unavailable; the panel buttons follow that state.
        Assert.Equal(CommandAvailability.Unavailable, commands.GetState(Pull).Availability);
        Assert.All(actions, a => Assert.False(a.Command.CanExecute(null)));
    }
}
