using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
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
        Tree.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        FlatList.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        Tree.SelectionChanged += (_, _) =>
        {
            if (Tree.SelectedItem is { } item && Tree.TreeContainerFromItem(item) is TreeViewItem { IsExpanded: false } container)
                container.IsExpanded = true;
        };
    }

    /// <summary>The most recent key menu, for tests.</summary>
    internal MenuFlyout? LastFlyout { get; private set; }

    /// <summary>
    /// Builds a fresh flyout for the right-clicked key and shows it. A <see cref="MenuFlyout"/> (like the rest of the app's menus)
    /// is used rather than a <see cref="ContextMenu"/>, whose popup never appeared on macOS.
    /// </summary>
    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control host || e.Source is not Visual source) return;
        var row = (Visual?)source.FindAncestorOfType<TreeViewItem>(includeSelf: true) ?? source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        var ns = (row as StyledElement)?.DataContext switch
        {
            NsTreeItem node => node.Namespace,
            NsFlatItem flat => flat.FullKey,
            _ => null
        };

        var flyout = new MenuFlyout();
        LastFlyout = flyout;
        flyout.Items.Add(new MenuItem { Header = "Add Key…", Command = vm.NewItemCommand });
        if (!string.IsNullOrEmpty(ns))
        {
            flyout.Items.Add(new Separator());
            flyout.Items.Add(new MenuItem { Header = "Rename…", Command = vm.RenameKeyCommand, CommandParameter = ns });
            flyout.Items.Add(new MenuItem { Header = "Duplicate", Command = vm.DuplicateKeyCommand, CommandParameter = ns });
            flyout.Items.Add(new MenuItem { Header = "Delete…", Command = vm.DeleteKeyCommand, CommandParameter = ns });
            flyout.Items.Add(new Separator());
            flyout.Items.Add(new MenuItem { Header = "Copy Key", Command = vm.CopyKeyCommand, CommandParameter = ns });
            flyout.Items.Add(new MenuItem { Header = "Translate Empty Values", Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => vm.TranslateKeyAsync(ns)) });
            flyout.Items.Add(new MenuItem { Header = "Hide Namespace", Command = vm.HideNamespaceCommand, CommandParameter = ns });
        }

        if (e.TryGetPosition(host, out _))
        {
            flyout.Placement = PlacementMode.Pointer;
            flyout.ShowAt(host, showAtPointer: true);
        }
        else
        {
            flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
            flyout.ShowAt((row as Control) ?? host);
        }
        e.Handled = true;
    }
}
