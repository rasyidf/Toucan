using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Toucan.Plugins.Desktop;

/// <summary>
/// What a plugin's views and view models may see and do in the open project. It deliberately exposes no view models or
/// stores of the host: edits go through the workspace API (v0.23, step 5) so they behave like native edits.
/// </summary>
public interface IPluginWorkspace
{
    bool HasWorkspace { get; }

    /// <summary>Identifies the open project (the same ID activators receive), or null when none is open.</summary>
    string? WorkspaceId { get; }

    /// <summary>Folder of the open project, or null.</summary>
    string? ProjectPath { get; }

    /// <summary>Name of the selected translation key, or null.</summary>
    string? SelectedKey { get; }

    /// <summary>Raised on the UI thread when the project opens or closes or the selected key changes.</summary>
    event EventHandler? Changed;

    /// <summary>Runs a command from the registry, as the menu would. Returns false when it was refused, failed or cancelled.</summary>
    Task<bool> ExecuteCommandAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handle to the plugin's status bar item. Change it from any thread; the host updates the display on the UI thread. An item
/// with no text, icon or badge is hidden.
/// </summary>
public interface IStatusBarItem
{
    string? Text { get; set; }
    string? Icon { get; set; }
    string? ToolTip { get; set; }
    string? Badge { get; set; }
    StatusBarSeverity BadgeSeverity { get; set; }
    bool IsVisible { get; set; }
}

/// <summary>Application services for plugin views.</summary>
public interface IDesktopHost
{
    /// <summary>The UI culture, for choosing text.</summary>
    CultureInfo Culture { get; }

    /// <summary>Opens a dialog the plugin registered and waits until it closes. Returns false when the dialog is unknown.</summary>
    Task<bool> ShowDialogAsync(string dialogId, object? parameter = null);
}

/// <summary>Passed to <see cref="IToucanDesktopPlugin.InitializeDesktop"/>. Everything registered here appears in the desktop app only.</summary>
public interface IDesktopPluginContext
{
    string PluginId { get; }

    /// <summary>The desktop contract version the host implements.</summary>
    Version HostContractVersion { get; }

    ILogger Logger { get; }

    IPluginWorkspace Workspace { get; }

    IDesktopHost Host { get; }

    /// <summary>The plugin's host services (settings, files, secrets, notifications, background work, diagnostics): the same object the main part gets.</summary>
    IPluginServices Services { get; }

    void AddSidePanel(SidePanelContribution contribution);

    void AddSettingsPage(SettingsPageContribution contribution);

    void AddInspectorSection(InspectorSectionContribution contribution);

    void AddDialog(DialogContribution contribution);

    void AddEditorAction(EditorActionContribution contribution);

    /// <summary>Adds an item to the status bar and returns the handle the plugin updates it through.</summary>
    IStatusBarItem AddStatusBarItem(StatusBarItemContribution contribution);
}

/// <summary>
/// Entry point of the desktop assembly named by the manifest's <c>desktop.entryAssembly</c>. Created with a parameterless
/// constructor after the main plugin has loaded, only by hosts with a UI.
/// </summary>
public interface IToucanDesktopPlugin
{
    void InitializeDesktop(IDesktopPluginContext context);
}
