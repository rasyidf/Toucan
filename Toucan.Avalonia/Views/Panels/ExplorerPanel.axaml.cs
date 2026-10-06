using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Views.Panels;

/// <summary>Key explorer: namespace tree, or a flat key list.</summary>
public partial class ExplorerPanel : UserControl
{
    public ExplorerPanel()
    {
        InitializeComponent();
        FlatList.SelectionChanged += (_, _) =>
        {
            if (FlatList.SelectedItem is NsFlatItem flat && DataContext is MainWindowViewModel vm)
                vm.SelectedNode = flat.Source;
        };
        // Attached menus open through Avalonia's own context-menu path (reliable on macOS). The tunnel handler
        // rebuilds the items for the clicked row just before the built-in handler opens the menu.
        Tree.ContextMenu = new ContextMenu();
        FlatList.ContextMenu = new ContextMenu();
        Tree.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        FlatList.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        Tree.SelectionChanged += (_, _) =>
        {
            if (Tree.SelectedItem is { } item && Tree.TreeContainerFromItem(item) is TreeViewItem { IsExpanded: false } container)
                container.IsExpanded = true;
        };
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || e.Source is not Visual source) return;
        var ns = source.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext switch
        {
            NsTreeItem node => node.Namespace,
            _ => (source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as NsFlatItem)?.FullKey
        };

        if ((sender as Control)?.ContextMenu is not { } menu) return;
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = "Add Key…", Command = vm.NewItemCommand });
        if (!string.IsNullOrEmpty(ns))
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Rename…", Command = vm.RenameKeyCommand, CommandParameter = ns });
            menu.Items.Add(new MenuItem { Header = "Duplicate", Command = vm.DuplicateKeyCommand, CommandParameter = ns });
            menu.Items.Add(new MenuItem { Header = "Delete…", Command = vm.DeleteKeyCommand, CommandParameter = ns });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Copy Key", Command = vm.CopyKeyCommand, CommandParameter = ns });
            menu.Items.Add(new MenuItem { Header = "Translate Empty Values", Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => vm.TranslateKeyAsync(ns)) });
            menu.Items.Add(new MenuItem { Header = "Hide Namespace", Command = vm.HideNamespaceCommand, CommandParameter = ns });
        }
    }
}
