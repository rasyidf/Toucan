using System.Reflection;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Commands;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Loads the desktop part of every plugin that loaded and has a <c>desktop</c> entry in its manifest, and registers what it
/// contributes. A part that cannot load or throws contributes nothing and never affects the rest of the application.
/// </summary>
internal static class DesktopPluginLoader
{
    private sealed class PluginSidePanel : SidePanelBase
    {
        public PluginSidePanel(string id, string title, string icon, SidePanelSlot slot, int order)
        {
            Id = id;
            Title = title;
            Icon = icon;
            DefaultSlot = slot;
            Order = order;
        }

        public override string Id { get; }
        public override string Title { get; }
        public override string Icon { get; }
        public override SidePanelSlot DefaultSlot { get; }
    }

    public static IReadOnlyList<DesktopLoadResult> LoadAll(IPluginCatalog catalog, DesktopContributions contributions, SidePanelRegistry panels,
        IPluginWorkspace workspace, IDesktopHost host, ICommandRegistry commands, ILoggerFactory? loggerFactory = null,
        IEnumerable<PluginServicesRegistration>? services = null)
    {
        var byPlugin = (services ?? []).ToDictionary(s => s.PluginId, s => s.Services, StringComparer.Ordinal);
        var results = new List<DesktopLoadResult>();
        foreach (var plugin in catalog.Plugins.Where(p => p is { Status: PluginStatus.Loaded, Manifest.Desktop: not null }))
        {
            var manifest = plugin.Manifest!;
            var result = LoadOne(plugin, manifest, contributions, panels, workspace, host, commands, loggerFactory ?? NullLoggerFactory.Instance,
                byPlugin.GetValueOrDefault(manifest.Id));
            contributions.RecordResult(result);
            results.Add(result);
        }
        return results;
    }

    // Plugin code is untrusted: anything its desktop part throws is a failure of that part only.
#pragma warning disable CA1031
    private static DesktopLoadResult LoadOne(PluginLoadResult plugin, PluginManifest manifest, DesktopContributions contributions, SidePanelRegistry panels,
        IPluginWorkspace workspace, IDesktopHost host, ICommandRegistry commands, ILoggerFactory loggerFactory, IPluginServices? services)
    {
        var desktop = manifest.Desktop!;
        var contract = desktop.ParsedContractVersion!;
        if (!DesktopContract.IsCompatible(contract))
            return new DesktopLoadResult(manifest.Id, DesktopLoadStatus.Incompatible,
                $"Its desktop part was built for desktop contract {contract}, but this Toucan implements {DesktopContract.Current}.");

        try
        {
            if (plugin.LoadContext is null)
                return new DesktopLoadResult(manifest.Id, DesktopLoadStatus.Failed, "The plugin's load context is not available.");

            var path = Path.GetFullPath(Path.Combine(plugin.Directory, desktop.EntryAssembly));
            if (!File.Exists(path))
                return new DesktopLoadResult(manifest.Id, DesktopLoadStatus.Failed, $"Desktop assembly '{desktop.EntryAssembly}' was not found.");

            var assembly = plugin.LoadContext.LoadFromAssemblyPath(path);
            var type = FindEntryType(assembly, desktop.EntryType, out var error);
            if (type is null) return new DesktopLoadResult(manifest.Id, DesktopLoadStatus.Failed, error);

            var instance = (IToucanDesktopPlugin)Activator.CreateInstance(type)!;
            return Initialize(manifest.Id, instance, contributions, panels, workspace, host, commands, loggerFactory.CreateLogger($"Plugin.{manifest.Id}"), services);
        }
        catch (Exception ex)
        {
            return new DesktopLoadResult(manifest.Id, DesktopLoadStatus.Failed, Describe(ex));
        }
    }

    /// <summary>Runs the plugin's desktop entry point and applies what it registered, all or nothing.</summary>
    internal static DesktopLoadResult Initialize(string pluginId, IToucanDesktopPlugin plugin, DesktopContributions contributions, SidePanelRegistry panels,
        IPluginWorkspace workspace, IDesktopHost host, ICommandRegistry commands, ILogger logger, IPluginServices? services = null)
    {
        var context = new Context(pluginId, workspace, host, logger, services, commands);
        try
        {
            plugin.InitializeDesktop(context);
        }
        catch (Exception ex)
        {
            return new DesktopLoadResult(pluginId, DesktopLoadStatus.Failed, Describe(ex));
        }

        try
        {
            Apply(pluginId, context, contributions, panels, commands);
        }
        catch (Exception ex)
        {
            contributions.RemovePlugin(pluginId);
            return new DesktopLoadResult(pluginId, DesktopLoadStatus.Failed, Describe(ex));
        }
        return new DesktopLoadResult(pluginId, DesktopLoadStatus.Loaded);
    }
#pragma warning restore CA1031

    private static void Apply(string pluginId, Context context, DesktopContributions contributions, SidePanelRegistry panels, ICommandRegistry commands)
    {
        foreach (var c in context.Panels)
        {
            var title = LocalizedText.Pick(c.LocalizedTitles, c.Title);
            contributions.AddPanel(c.Id, _ => c.CreateContent(context.Workspace),
                _ => [.. c.Actions.Select(a => new PanelActionItem(
                    Enum.TryParse<FASymbol>(a.Icon, ignoreCase: true, out var icon) ? icon : FASymbol.Document,
                    a.ToolTip, RegistryCommand.For(commands, a.CommandId)))],
                pluginId: pluginId);
            panels.Register(new PluginSidePanel(c.Id, title, c.Icon, c.Slot == PanelSlot.Left ? SidePanelSlot.Left : SidePanelSlot.Right, c.Order));
        }

        foreach (var c in context.Inspector)
            contributions.AddInspectorSection(new InspectorSectionItem(pluginId, c.Id, LocalizedText.Pick(c.LocalizedTitles, c.Title), c.Order, () => c.CreateContent(context.Workspace)));

        foreach (var c in context.Settings)
            contributions.AddSettingsSection(new SettingsSectionItem(pluginId, c.Id, LocalizedText.Pick(c.LocalizedTitles, c.Title), c.Description, () => c.CreateContent(context.Workspace)));

        foreach (var item in context.StatusItems) contributions.AddStatusItem(item);

        foreach (var c in context.Dialogs) contributions.AddDialog(pluginId, c);
        foreach (var c in context.Actions) contributions.AddKeyAction(pluginId, c);
    }

    private static Type? FindEntryType(Assembly assembly, string? entryType, out string? error)
    {
        error = null;
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        var candidates = types.Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IToucanDesktopPlugin).IsAssignableFrom(t)
                                          && t.GetConstructor(Type.EmptyTypes) is not null).ToList();
        if (entryType is { Length: > 0 })
        {
            var match = candidates.FirstOrDefault(t => string.Equals(t.FullName, entryType, StringComparison.Ordinal));
            if (match is null) error = $"'{entryType}' is not a public IToucanDesktopPlugin with a parameterless constructor in {assembly.GetName().Name}.";
            return match;
        }

        if (candidates.Count == 1) return candidates[0];
        error = candidates.Count == 0
            ? $"{assembly.GetName().Name} has no public IToucanDesktopPlugin implementation with a parameterless constructor."
            : $"{assembly.GetName().Name} has {candidates.Count} IToucanDesktopPlugin implementations; set 'desktop.entryType' in the manifest.";
        return null;
    }

    private static string Describe(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? Describe(inner) : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>Collects one plugin's registrations and checks them; nothing reaches the host unless the plugin's entry point returns normally.</summary>
    private sealed class Context(string pluginId, IPluginWorkspace workspace, IDesktopHost host, ILogger logger, IPluginServices? services, ICommandRegistry commands) : IDesktopPluginContext
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public string PluginId { get; } = pluginId;
        public Version HostContractVersion => DesktopContract.Current;
        public ILogger Logger { get; } = logger;
        public IPluginWorkspace Workspace { get; } = workspace;
        public IDesktopHost Host { get; } = host;
        public IPluginServices Services => services ?? throw new InvalidOperationException("Plugin services are not available in this host.");
        public List<PluginStatusBarItem> StatusItems { get; } = [];

        /// <summary>Returns the item the status bar will show; it is added to the bar only if the whole desktop entry point succeeds.</summary>
        public IStatusBarItem AddStatusBarItem(StatusBarItemContribution contribution)
        {
            var claimed = Claim(contribution);
            var command = claimed.CommandId is { Length: > 0 } id ? RegistryCommand.For(commands, id) : null;
            var item = new PluginStatusBarItem(PluginId, claimed.Id, LocalizedText.Pick(claimed.LocalizedTitles, claimed.Title), claimed.Side, claimed.Order, command)
            {
                Text = claimed.Text, Icon = claimed.Icon, Badge = claimed.Badge, BadgeSeverity = claimed.BadgeSeverity,
            };
            StatusItems.Add(item);
            return item;
        }

        public List<SidePanelContribution> Panels { get; } = [];
        public List<SettingsPageContribution> Settings { get; } = [];
        public List<InspectorSectionContribution> Inspector { get; } = [];
        public List<DialogContribution> Dialogs { get; } = [];
        public List<EditorActionContribution> Actions { get; } = [];

        public void AddSidePanel(SidePanelContribution contribution) => Panels.Add(Claim(contribution));
        public void AddSettingsPage(SettingsPageContribution contribution) => Settings.Add(Claim(contribution));
        public void AddInspectorSection(InspectorSectionContribution contribution) => Inspector.Add(Claim(contribution));
        public void AddDialog(DialogContribution contribution) => Dialogs.Add(Claim(contribution));
        public void AddEditorAction(EditorActionContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            if (string.IsNullOrWhiteSpace(contribution.CommandId))
                throw new PluginRegistrationException($"Editor action '{contribution.Id}' needs a command ID.");
            Actions.Add(Claim(contribution));
        }

        private T Claim<T>(T contribution) where T : DesktopContribution
        {
            ArgumentNullException.ThrowIfNull(contribution);
            if (string.IsNullOrWhiteSpace(contribution.Id) || !contribution.Id.StartsWith(PluginId + ".", StringComparison.Ordinal))
                throw new PluginRegistrationException($"'{contribution.Id}' must start with the plugin ID '{PluginId}.'.");
            if (string.IsNullOrWhiteSpace(contribution.Title))
                throw new PluginRegistrationException($"'{contribution.Id}' needs a title.");
            if (!_ids.Add(contribution.Id))
                throw new PluginRegistrationException($"'{contribution.Id}' is registered twice by this plugin.");
            return contribution;
        }
    }
}
