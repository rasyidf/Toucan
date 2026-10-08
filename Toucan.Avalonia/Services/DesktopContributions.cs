using System.Windows.Input;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Toucan.Avalonia.ViewModels;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.Services;

/// <summary>A toolbar button of a side panel.</summary>
internal sealed record PanelActionItem(FASymbol Icon, string ToolTip, ICommand Command);

/// <summary>An action on the selected key (inspector button, key context menu).</summary>
internal sealed record KeyActionItem(string Title, FASymbol? Icon, ICommand Command, string CommandId, int Order);

internal sealed record InspectorSectionItem(string PluginId, string Id, string Title, int Order, Func<Control> Create);

internal sealed record SettingsSectionItem(string PluginId, string Id, string Title, string? Description, Func<Control> Create);

internal enum DesktopLoadStatus
{
    Loaded,
    Failed,
    Incompatible,
}

/// <summary>Outcome of loading one plugin's desktop part, shown next to the plugin in Settings.</summary>
internal sealed record DesktopLoadResult(string PluginId, DesktopLoadStatus Status, string? Error = null);

/// <summary>
/// Everything that appears in the main window beyond the fixed layout: side panels (with their toolbars), inspector
/// sections, plugin settings, dialogs and key actions. The built-in panels register here exactly like a plugin's, so
/// <c>MainWindow</c> has no panel-specific code.
/// </summary>
internal sealed class DesktopContributions
{
    private sealed record PanelEntry(string Id, string? PluginId, Func<MainWindowViewModel, Control> Create,
        Func<MainWindowViewModel, IReadOnlyList<PanelActionItem>> Actions, Action<MainWindowViewModel>? OnShow);

    private readonly Dictionary<string, PanelEntry> _panels = new(StringComparer.Ordinal);
    private readonly List<InspectorSectionItem> _inspector = [];
    private readonly List<SettingsSectionItem> _settings = [];
    private readonly Dictionary<string, (string PluginId, DialogContribution Contribution)> _dialogs = new(StringComparer.Ordinal);
    private readonly List<(string PluginId, EditorActionContribution Contribution)> _keyActions = [];
    private readonly List<DesktopLoadResult> _results = [];

    public static DesktopContributions Instance { get; } = new();

    public event EventHandler? Changed;

    public IReadOnlyList<DesktopLoadResult> LoadResults => _results;

    // --- side panels ------------------------------------------------------------------------------------------

    public void AddPanel(string id, Func<MainWindowViewModel, Control> create, Func<MainWindowViewModel, IReadOnlyList<PanelActionItem>>? actions = null,
        Action<MainWindowViewModel>? onShow = null, string? pluginId = null)
    {
        if (!_panels.TryAdd(id, new PanelEntry(id, pluginId, create, actions ?? (_ => []), onShow)))
            throw new InvalidOperationException($"Panel '{id}' is already registered.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool HasPanel(string id) => _panels.ContainsKey(id);

    public Control? CreatePanel(string id, MainWindowViewModel vm) => _panels.TryGetValue(id, out var e) ? e.Create(vm) : null;

    public IReadOnlyList<PanelActionItem> PanelActions(string id, MainWindowViewModel vm) => _panels.TryGetValue(id, out var e) ? e.Actions(vm) : [];

    public void PanelShown(string id, MainWindowViewModel vm)
    {
        if (_panels.TryGetValue(id, out var e)) e.OnShow?.Invoke(vm);
    }

    // --- inspector, settings, dialogs, key actions (plugin-only) ---------------------------------------------

    public IReadOnlyList<InspectorSectionItem> InspectorSections => [.. _inspector.OrderBy(s => s.Order).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<SettingsSectionItem> SettingsSections => _settings;

    public IReadOnlyList<(string PluginId, EditorActionContribution Contribution)> KeyActions => _keyActions;

    public void AddInspectorSection(InspectorSectionItem item)
    {
        _inspector.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddSettingsSection(SettingsSectionItem item)
    {
        _settings.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddDialog(string pluginId, DialogContribution contribution) => _dialogs[contribution.Id] = (pluginId, contribution);

    public (string PluginId, DialogContribution Contribution)? FindDialog(string id) => _dialogs.TryGetValue(id, out var d) ? d : null;

    public void AddKeyAction(string pluginId, EditorActionContribution contribution)
    {
        _keyActions.Add((pluginId, contribution));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordResult(DesktopLoadResult result)
    {
        _results.RemoveAll(r => r.PluginId == result.PluginId);
        _results.Add(result);
    }

    /// <summary>Removes everything registered by one plugin (a desktop part that failed halfway must contribute nothing).</summary>
    public void RemovePlugin(string pluginId)
    {
        foreach (var id in _panels.Where(p => p.Value.PluginId == pluginId).Select(p => p.Key).ToList()) _panels.Remove(id);
        _inspector.RemoveAll(s => s.PluginId == pluginId);
        _settings.RemoveAll(s => s.PluginId == pluginId);
        foreach (var id in _dialogs.Where(d => d.Value.PluginId == pluginId).Select(d => d.Key).ToList()) _dialogs.Remove(id);
        _keyActions.RemoveAll(a => a.PluginId == pluginId);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
