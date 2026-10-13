using System.Windows.Input;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Commands;
using Toucan.Plugins;

namespace Toucan.Avalonia.Services;

/// <summary>
/// The application's own commands, registered in the same registry plugin commands use. Each one forwards to a
/// view-model command, so availability follows that command's <c>CanExecute</c>.
/// </summary>
internal static class BuiltInCommands
{
    internal sealed record BuiltIn(string Id, string Category, string Action, string? Shortcut, Func<MainWindowViewModel, ICommand> Command,
        bool SkipInTextBox = false, object? Parameter = null);

    private static bool Mac => PlatformService.IsMacOS;

    public static IReadOnlyList<BuiltIn> All { get; } = Create();

    private static List<BuiltIn> Create()
    {
        static BuiltIn B(string category, string action, string? shortcut, Func<MainWindowViewModel, ICommand> command, bool skipInTextBox = false, object? parameter = null) =>
            new(IdFor(category, action), category, action, shortcut, command, skipInTextBox, parameter);

        return
        [
            B("File", "New Project", "Mod+Shift+N", vm => vm.NewFolderCommand),
            B("File", "Open Folder", "Mod+O", vm => vm.OpenFolderCommand),
            B("File", "Open Recent", "Mod+R", vm => vm.OpenRecentCommand),
            B("File", "Save", "Mod+S", vm => vm.SaveCommand),
            B("File", "Save As", "Mod+Shift+S", vm => vm.SaveToCommand),
            B("File", "Close Project", "Mod+W", vm => vm.CloseProjectCommand),
            B("File", "Refresh", "F5", vm => vm.RefreshCommand),
            B("Edit", "Undo", "Mod+Z", vm => vm.UndoCommand, skipInTextBox: true),
            B("Edit", "Redo", Mac ? "Mod+Shift+Z" : "Mod+Y", vm => vm.RedoCommand, skipInTextBox: true),
            B("Edit", "Add Translation Key", "Mod+I", vm => vm.NewItemCommand),
            B("Edit", "Add Language", "Mod+L", vm => vm.NewLanguageCommand),
            B("Edit", "Rename", "F2", vm => vm.RenameItemCommand, skipInTextBox: true),
            B("Edit", "Delete", "Delete", vm => vm.DeleteItemCommand, skipInTextBox: true),
            B("Edit", "Duplicate", "Mod+D", vm => vm.DuplicateItemCommand),
            B("Edit", "Copy Template 1", "Mod+D1", vm => vm.CopyAsTemplateCommand, parameter: 0),
            B("Edit", "Copy Template 2", "Mod+D2", vm => vm.CopyAsTemplateCommand, parameter: 1),
            B("Edit", "Copy Template 3", "Mod+D3", vm => vm.CopyAsTemplateCommand, parameter: 2),
            B("Edit", "Copy Template 4", "Mod+D4", vm => vm.CopyAsTemplateCommand, parameter: 3),
            B("Edit", "Copy Template 5", "Mod+D5", vm => vm.CopyAsTemplateCommand, parameter: 4),
            B("Find", "Find", "Mod+F", vm => vm.FocusSearchCommand),
            B("Find", "Search & Replace", "Mod+Shift+F", vm => vm.OpenSearchPanelCommand),
            B("Find", "Next Page", "F3", vm => vm.NextPageCommand),
            B("Find", "Clear Filter", "Escape", vm => vm.ClearFilterCommand, skipInTextBox: true),
            B("View", "Command Palette", "Mod+Shift+P", vm => vm.ToggleCommandPaletteCommand),
            B("View", "Keyboard Shortcuts", "Mod+OemQuestion", vm => vm.ToggleShortcutSheetCommand),
            B("View", "Toggle Left Panel", "Mod+B", _ => PanelService.Instance.ToggleSidebarCommand),
            B("View", "Toggle Right Panel", "Mod+Alt+B", _ => PanelService.Instance.ToggleInspectorCommand),
            B("View", "Focused Editor", "Mod+E", vm => vm.ToggleFocusedEditorCommand),
            B("View", "Zen Mode", "Mod+Shift+Enter", vm => vm.ToggleZenModeCommand),
            B("View", "Fullscreen", Mac ? "Mod+Ctrl+F" : "F11", vm => vm.ToggleFullscreenCommand),
            B("View", "Editor Mode", "Mod+Alt+D1", vm => vm.SwitchToEditorModeCommand),
            B("View", "Review Mode", "Mod+Alt+D2", vm => vm.SwitchToReviewModeCommand),
            B("View", "Audit Mode", "Mod+Alt+D3", vm => vm.SwitchToAuditModeCommand),
            B("Translate", "Pre-translate", "Mod+Shift+T", vm => vm.PreTranslateBulkCommand),
            B("Translate", "Run Validation", "F7", vm => vm.RunValidationCommand),
            B("Settings", "Preferences", "Mod+OemComma", vm => vm.ShowPreferencesCommand),
        ];
    }

    public static string IdFor(string category, string action)
    {
        var slug = action.ToLowerInvariant().Replace("&", "and", StringComparison.Ordinal).Replace(' ', '-');
        return $"toucan.{category.ToLowerInvariant()}.{slug}";
    }

    public static BuiltIn? FindByAction(string action) => All.FirstOrDefault(b => b.Action == action);

    /// <summary>
    /// Registers every built-in command, replacing the ones registered for an earlier window. With no view model
    /// the commands exist (so shortcuts can be listed) but report themselves unavailable.
    /// </summary>
    public static void Bind(ICommandRegistry registry, MainWindowViewModel? vm)
    {
        foreach (var b in All)
        {
            registry.Unregister(b.Id);
            var command = vm is null ? null : b.Command(vm);
            registry.Register(new CommandDefinition
            {
                Id = b.Id,
                Title = b.Action,
                Category = b.Category,
                DefaultShortcut = b.Shortcut,
                RequiresWorkspace = false,
                Placements = CommandPlacement.CommandPalette | CommandPlacement.Menu,
            }, new ViewModelCommandHandler(command, b.Parameter));
        }
    }

    private sealed class ViewModelCommandHandler(ICommand? command, object? parameter) : ICommandHandler
    {
        public ICommand? Command { get; } = command;

        public CommandState GetState(ICommandContext context) =>
            Command is null ? new CommandState(CommandAvailability.Unavailable, "Not ready yet.")
            : Command.CanExecute(parameter) ? CommandState.Available
            : new CommandState(CommandAvailability.Unavailable);

        public Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
        {
            Command?.Execute(parameter);
            return Task.CompletedTask;
        }
    }

    /// <summary>The view-model command behind a built-in registration, for forwarding <c>CanExecuteChanged</c>.</summary>
    public static ICommand? InnerCommand(RegisteredCommand command) => (command.Handler as ViewModelCommandHandler)?.Command;
}
