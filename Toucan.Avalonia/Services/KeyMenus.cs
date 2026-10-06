using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Right-click menus for translation keys, shared by the Explorer and the editor cards. They are <see cref="MenuFlyout"/>s
/// (like the rest of the app's menus) because a hand-built <see cref="ContextMenu"/> never rendered on macOS.
/// </summary>
internal static class KeyMenus
{
    private static MenuItem Item(string header, FASymbol icon, System.Windows.Input.ICommand command, object? parameter = null) => new()
    {
        Header = Loc.T(header),
        Icon = new FASymbolIcon { Symbol = icon },
        Command = command,
        CommandParameter = parameter,
    };

    /// <summary>Menu for a row in the key explorer; <paramref name="ns"/> is null for empty space.</summary>
    public static MenuFlyout ForExplorer(MainWindowViewModel vm, string? ns)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("Add Key…", FASymbol.Add, vm.NewItemCommand));
        if (string.IsNullOrEmpty(ns)) return flyout;

        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Rename…", FASymbol.Rename, vm.RenameKeyCommand, ns));
        flyout.Items.Add(Item("Duplicate", FASymbol.Copy, vm.DuplicateKeyCommand, ns));
        flyout.Items.Add(Item("Delete…", FASymbol.Delete, vm.DeleteKeyCommand, ns));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Copy Key", FASymbol.Link, vm.CopyKeyCommand, ns));
        flyout.Items.Add(Item("Translate Empty Values", FASymbol.Character, new AsyncRelayCommand(() => vm.TranslateKeyAsync(ns))));
        flyout.Items.Add(Item("Hide Namespace", FASymbol.View, vm.HideNamespaceCommand, ns));
        return flyout;
    }

    /// <summary>Menu for an editor card.</summary>
    public static MenuFlyout ForCard(MainWindowViewModel vm, LanguageGroupViewModel group)
    {
        var ns = group.Namespace;
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("Translate Empty Values", FASymbol.Character, group.TranslateKeyCommand));
        flyout.Items.Add(Item("Copy Key", FASymbol.Link, vm.CopyKeyCommand, ns));

        var copyAs = new MenuItem { Header = Loc.T("Copy Key As"), Icon = new FASymbolIcon { Symbol = FASymbol.Copy } };
        for (var i = 0; i < vm.CopyTemplates.Count; i++)
        {
            copyAs.Items.Add(new MenuItem
            {
                Header = vm.CopyTemplates[i].Replace("%1", ns, StringComparison.Ordinal),
                Command = vm.CopyAsTemplateCommand,
                CommandParameter = i,
            });
        }
        // Copy templates act on the selected card, so make this card the selection before any of them runs.
        vm.SelectedGroup = group;
        flyout.Items.Add(copyAs);

        flyout.Items.Add(new Separator());
        if (!group.IsPluralGroup)
        {
            flyout.Items.Add(Item("Rename…", FASymbol.Rename, vm.RenameKeyCommand, ns));
            flyout.Items.Add(Item("Duplicate", FASymbol.Copy, vm.DuplicateKeyCommand, ns));
        }
        flyout.Items.Add(Item("Delete…", FASymbol.Delete, vm.DeleteKeyCommand, ns));
        return flyout;
    }

    /// <summary>Opens at the pointer for a real right-click, or under <paramref name="anchor"/> for a keyboard-invoked menu.</summary>
    public static void Show(MenuFlyout flyout, Control host, Control anchor, ContextRequestedEventArgs e)
    {
        if (e.TryGetPosition(host, out _))
        {
            flyout.Placement = PlacementMode.Pointer;
            flyout.ShowAt(host, showAtPointer: true);
        }
        else
        {
            flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
            flyout.ShowAt(anchor);
        }
        e.Handled = true;
    }
}
