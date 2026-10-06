using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views;

/// <summary>
/// Builds the application menu from one definition: a native global menu bar on macOS,
/// and an in-window <see cref="Menu"/> on Windows and Linux.
/// </summary>
internal static class MainMenu
{
    private sealed record Item(
        string Header,
        ICommand? Command = null,
        object? Parameter = null,
        string? Action = null,
        IReadOnlyList<Item>? Children = null,
        bool IsSeparator = false);

    private static readonly Item Separator = new("-", IsSeparator: true);

    private static readonly HashSet<KeyGesture> s_nativeGestures = [];

    /// <summary>Gestures owned by the native menu (macOS only); the window must not bind them again.</summary>
    public static IReadOnlySet<KeyGesture> NativeGestures => s_nativeGestures;

    public static void Attach(Window window, MainWindowViewModel vm, ContentControl host)
    {
        void Rebuild()
        {
            var items = Build(window, vm);
            if (PlatformService.IsMacOS)
            {
                // macOS can't swap the root menu after it's exported; refill the existing one instead.
                s_nativeGestures.Clear();
                // Cmd+, is owned by the application menu declared in App.axaml.
                s_nativeGestures.Add(new KeyGesture(Key.OemComma, KeyModifiers.Meta));
                var root = NativeMenu.GetMenu(window);
                if (root == null)
                {
                    root = new NativeMenu();
                    FillNative(root, items);
                    NativeMenu.SetMenu(window, root);
                }
                else
                {
                    root.Items.Clear();
                    FillNative(root, items);
                }
                host.IsVisible = false;
            }
            else
            {
                host.Content = new Menu { ItemsSource = items.Select(ToMenuItem).ToList() };
            }
        }

        Rebuild();
        // The Open Recent submenu lists projects, so rebuild when the list changes.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.RecentProjects) or nameof(MainWindowViewModel.CopyTemplates) or nameof(MainWindowViewModel.ProjectSettings))
                Rebuild();
        };
    }

    private static List<Item> Build(Window window, MainWindowViewModel vm)
    {
        var mac = PlatformService.IsMacOS;
        var recent = vm.RecentProjects.Select(p => new Item(p.Path, vm.OpenRecentProjectCommand, p.Path)).ToList();
        if (recent.Count > 0)
        {
            recent.Add(Separator);
            recent.Add(new Item("Clear Recent", vm.ClearRecentProjectsCommand));
        }
        else
        {
            recent.Add(new Item("No recent projects"));
        }

        var templates = vm.CopyTemplates.Select((t, i) => new Item($"{i + 1}. {t}", vm.CopyAsTemplateCommand, i, i < 5 ? $"Copy Template {i + 1}" : null)).ToList();

        var file = new List<Item>
        {
            new("New Project…", vm.NewFolderCommand, Action: "New Project"),
            new("Open Folder…", vm.OpenFolderCommand, Action: "Open Folder"),
            new("Open Project File…", vm.OpenProjectFileCommand),
            new("Import Existing Project…", vm.ImportProjectCommand),
            new("Open Recent", Children: recent),
            Separator,
            new("Save", vm.SaveCommand, Action: "Save"),
            new("Save As…", vm.SaveToCommand, Action: "Save As"),
            Separator,
            new("Import Translations…", vm.ImportCommand),
            new("Import from Excel…", vm.ImportExcelCommand),
            new("Export…", vm.ExportCommand),
            new("Export to Excel…", vm.ExportExcelCommand),
            Separator,
            new($"Reveal in {PlatformService.FileManagerName}", vm.RevealInFileManagerCommand),
            new("Reload from Disk", vm.RefreshCommand, Action: "Refresh"),
            new("Close Project", vm.CloseProjectCommand, Action: "Close Project"),
        };
        if (!mac)
        {
            file.Add(Separator);
            file.Add(new Item("Exit", vm.ExitCommand));
        }

        var edit = new List<Item>
        {
            new("Undo", SmartTextCommand(window, vm.UndoCommand, t => t.Undo()), Action: "Undo"),
            new("Redo", SmartTextCommand(window, vm.RedoCommand, t => t.Redo()), Action: "Redo"),
            Separator,
        };
        if (mac)
        {
            // The native menu owns Cmd+X/C/V/A on macOS, so route them to the focused text box.
            edit.Add(new Item("Cut", SmartTextCommand(window, null, t => t.Cut()), Action: "__cut"));
            edit.Add(new Item("Copy", SmartTextCommand(window, null, t => t.Copy()), Action: "__copy"));
            edit.Add(new Item("Paste", SmartTextCommand(window, null, t => t.Paste()), Action: "__paste"));
            edit.Add(new Item("Select All", SmartTextCommand(window, null, t => t.SelectAll()), Action: "__selectall"));
            edit.Add(Separator);
        }
        edit.AddRange(
        [
            new("Cut Key Values", vm.EditCutCommand),
            new("Copy Key Values", vm.EditCopyCommand),
            new("Paste Key Values", vm.EditPasteCommand),
            new("Copy Key As", Children: templates),
            Separator,
            new("Add Translation Key…", vm.NewItemCommand, Action: "Add Translation Key"),
            new("Rename Key…", vm.RenameItemCommand, Action: "Rename"),
            new("Duplicate Key", vm.DuplicateItemCommand, Action: "Duplicate"),
            new("Delete Key…", vm.DeleteItemCommand, Action: "Delete"),
            new("Hide Namespace", vm.HideNamespaceCommand),
            Separator,
            new("Add Language…", vm.NewLanguageCommand, Action: "Add Language"),
            new("Manage Languages…", vm.ManageLanguagesCommand),
            Separator,
            new("Transform Values", Children:
            [
                new("lowercase", vm.ConvertLowercaseCommand),
                new("UPPERCASE", vm.ConvertUppercaseCommand),
                new("Sentence case", vm.ConvertSentenceCaseCommand),
                new("Title Case", vm.ConvertTitleCaseCommand),
                Separator,
                new("Trim Whitespace", vm.TrimWhitespaceCommand),
                new("Trim Line by Line", vm.TrimLineByLineCommand),
                new("Simplify Whitespace", vm.SimplifyWhitespaceCommand),
            ]),
            new("Generate Plural Forms", vm.GeneratePluralFormsCommand),
            new("Generate Gender Forms", vm.GenerateGenderFormsCommand),
            Separator,
            new("Add Missing Entries", vm.AddMissingTranslationsCommand),
            new("Delete Empty Keys…", vm.DeleteUnusedTranslationsCommand),
        ]);

        var find = new List<Item>
        {
            new("Filter Keys", vm.FocusSearchCommand, Action: "Find"),
            new("Search & Replace", vm.OpenSearchPanelCommand, Action: "Search & Replace"),
            Separator,
            new("Show Untranslated", vm.ShowUntranslatedCommand),
            new("Show Needs Review", vm.ShowNeedsReviewCommand),
            new("Show Approved", vm.ShowApprovedCommand),
            new("Show Translated", vm.ShowTranslatedCommand),
            new("Show Machine Translated", vm.ShowMachineTranslatedCommand),
            new("Show Changed This Session", vm.ShowChangedThisSessionCommand),
            Separator,
            new("Clear Filter", vm.ClearFilterCommand, Action: "Clear Filter"),
        };

        var view = new List<Item>
        {
            new("Toggle Tree / List", vm.ToggleViewModeCommand),
            new("Toggle Left Panel", PanelService.Instance.ToggleSidebarCommand, Action: "Toggle Left Panel"),
            new("Toggle Right Panel", PanelService.Instance.ToggleInspectorCommand, Action: "Toggle Right Panel"),
            new("Toggle Status Bar", PanelService.Instance.ToggleStatusBarCommand),
            Separator,
            new("Editor Mode", vm.SwitchToEditorModeCommand, Action: "Editor Mode"),
            new("Review Mode", vm.SwitchToReviewModeCommand, Action: "Review Mode"),
            new("Audit Mode", vm.SwitchToAuditModeCommand, Action: "Audit Mode"),
            Separator,
            new("Focused Editor", vm.ToggleFocusedEditorCommand, Action: "Focused Editor"),
            new("Zen Mode", vm.ToggleZenModeCommand, Action: "Zen Mode"),
            new("Multi-select", vm.ToggleMultiSelectCommand),
            new("Show All Items (No Paging)", vm.ToggleInfiniteScrollCommand),
            new("Full Screen", vm.ToggleFullscreenCommand, Action: "Fullscreen"),
        };

        var translate = new List<Item>
        {
            new("Pre-translate…", vm.PreTranslateBulkCommand, Action: "Pre-translate"),
            new("Translate Selected Key", vm.TranslateSelectedKeyCommand),
            new("Fill Empty Values in View", vm.PreTranslateVisibleCommand),
            new("Approve All in View", vm.ApproveVisibleCommand),
            Separator,
            new("Run Validation", vm.RunValidationCommand, Action: "Run Validation"),
            new("Analyze with AI…", vm.AnalyzeTranslationsCommand),
            new("Statistics…", vm.GenerateStatisticsBulkCommand),
            Separator,
            new("Scan Source Code", vm.ScanSourceCodeCommand),
            new("Translation Memory", Children:
            [
                new("Learn from This Project", vm.LearnProjectIntoMemoryCommand),
                new("Import TMX…", vm.ImportTmxCommand),
                new("Export TMX…", vm.ExportTmxCommand),
                Separator,
                new("Clear…", vm.ClearTmCommand),
            ]),
            Separator,
            new("Translation Providers…", new RelayCommand(() => _ = App.Dialogs.ShowProviderSettingsAsync(vm.HasProject ? vm.CurrentPath : null))),
        };

        var settings = new List<Item>
        {
            new("Project Properties…", vm.ShowProjectPropertiesCommand),
            new("Plugins…", vm.ShowPluginsCommand),
        };
        if (!mac)
        {
            settings.Insert(0, new Item("Preferences…", vm.ShowPreferencesCommand, Action: "Preferences"));
        }

        var help = new List<Item>
        {
            new("Documentation", vm.HelpHomepageCommand),
            new("Report an Issue", vm.ReportIssueCommand),
        };
        if (!mac) help.Add(new Item("About Toucan", vm.HelpAboutCommand));

        return
        [
            new("_File", Children: file),
            new("_Edit", Children: edit),
            new("F_ind", Children: find),
            new("_View", Children: view),
            new("_Translate", Children: translate),
            new(mac ? "Project" : "_Settings", Children: settings),
            new("_Help", Children: help),
        ];
    }

    /// <summary>Uses the focused text box when there is one (text undo, clipboard), otherwise the app command.</summary>
    private static RelayCommand SmartTextCommand(Window window, ICommand? fallback, Action<TextBox> textAction) => new(() =>
    {
        if (window.FocusManager?.GetFocusedElement() is TextBox tb) textAction(tb);
        else if (fallback?.CanExecute(null) == true) fallback.Execute(null);
    });

    private static KeyGesture? GestureOf(string? action)
    {
        var primary = PlatformService.IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control;
        return action switch
        {
            null => null,
            "__cut" => new KeyGesture(Key.X, primary),
            "__copy" => new KeyGesture(Key.C, primary),
            "__paste" => new KeyGesture(Key.V, primary),
            "__selectall" => new KeyGesture(Key.A, primary),
            _ => KeybindingService.GestureFor(action)
        };
    }

    private static object ToMenuItem(Item item)
    {
        if (item.IsSeparator) return new Separator();
        var mi = new MenuItem
        {
            Header = Loc.T(item.Header),
            Command = item.Command,
            CommandParameter = item.Parameter,
            InputGesture = GestureOf(item.Action),
            IsEnabled = item.Command != null || item.Children != null
        };
        if (item.Children != null) mi.ItemsSource = item.Children.Select(ToMenuItem).ToList();
        return mi;
    }

    private static NativeMenu ToNative(IEnumerable<Item> items)
    {
        var menu = new NativeMenu();
        FillNative(menu, items);
        return menu;
    }

    private static void FillNative(NativeMenu menu, IEnumerable<Item> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            var header = Loc.T(item.Header).Replace("_", string.Empty, StringComparison.Ordinal);
            if (item.Children != null)
            {
                menu.Items.Add(new NativeMenuItem(header) { Menu = ToNative(item.Children) });
                continue;
            }
            menu.Items.Add(Native(header, item.Command, item.Action, item.Parameter));
        }
    }

    private static NativeMenuItem Native(string header, ICommand? command, string? action, object? parameter = null)
    {
        var mi = new NativeMenuItem(header)
        {
            Command = command,
            CommandParameter = parameter,
            IsEnabled = command != null
        };

        // Keys that edit text (Delete, Escape, F2) must reach text boxes, so they stay window bindings.
        var gesture = GestureOf(action);
        var isTextKey = action != null && KeybindingService.IsTextEditingKey(action) && action is not ("Undo" or "Redo");
        if (gesture != null && !isTextKey)
        {
            mi.Gesture = gesture;
            s_nativeGestures.Add(gesture);
        }
        return mi;
    }
}
