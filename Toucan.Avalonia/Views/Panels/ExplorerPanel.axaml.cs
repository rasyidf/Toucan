using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toucan.Avalonia.Services;
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

        LastFlyout = KeyMenus.ForExplorer(vm, ns);
        KeyMenus.Show(LastFlyout, host, (row as Control) ?? host, e);
    }
}
