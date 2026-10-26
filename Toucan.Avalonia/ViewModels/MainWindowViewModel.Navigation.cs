using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private readonly WorkspaceNavigator _workspaceNavigator = new();
    private Guid _navigationSession;
    private bool _refreshingNavigation;
    private HashSet<Guid>? _documentSelection;

    [ObservableProperty] private bool hasDocumentNavigation;
    [ObservableProperty] private WorkspaceNavigationLayout navigationLayout;
    [ObservableProperty] private ObservableCollection<WorkspaceNavigationItemViewModel> documentNavigationItems = [];
    [ObservableProperty] private WorkspaceNavigationItemViewModel? selectedDocumentNode;

    public bool IsNamespaceNavigation => NavigationLayout == WorkspaceNavigationLayout.NamespaceFirst;
    public bool IsFileNavigation => NavigationLayout == WorkspaceNavigationLayout.FileFirst;

    [RelayCommand]
    private void SetNavigationLayout(WorkspaceNavigationLayout layout)
    {
        NavigationLayout = layout;
        OnPropertyChanged(nameof(IsNamespaceNavigation));
        OnPropertyChanged(nameof(IsFileNavigation));
    }

    partial void OnNavigationLayoutChanging(WorkspaceNavigationLayout value) => FlushPendingEdits();
    partial void OnNavigationLayoutChanged(WorkspaceNavigationLayout value)
    {
        RefreshDocumentNavigation();
        OnPropertyChanged(nameof(IsNamespaceNavigation));
        OnPropertyChanged(nameof(IsFileNavigation));
    }

    partial void OnSelectedDocumentNodeChanging(WorkspaceNavigationItemViewModel? value)
    {
        if (!_refreshingNavigation) FlushPendingEdits();
    }

    partial void OnSelectedDocumentNodeChanged(WorkspaceNavigationItemViewModel? value)
    {
        if (_refreshingNavigation) return;
        _documentSelection = value?.Node.EntryIds.ToHashSet();
        SelectedNode = null;
        SearchText = string.Empty;
        Search(string.Empty, true);
    }

    private void ClearDocumentSelection()
    {
        _refreshingNavigation = true;
        try { SelectedDocumentNode = null; _documentSelection = null; }
        finally { _refreshingNavigation = false; }
    }

    private void RefreshDocumentNavigation()
    {
        var snapshot = Workspace.SnapshotEntriesAsync().GetAwaiter().GetResult();
        var source = Workspace.Source;
        HasDocumentNavigation = snapshot is not null && source is not null;
        _refreshingNavigation = true;
        try
        {
            if (!HasDocumentNavigation)
            {
                _navigationSession = Guid.Empty; _documentSelection = null;
                SelectedDocumentNode = null; DocumentNavigationItems = [];
                return;
            }
            if (_navigationSession != snapshot!.SessionId)
            {
                _documentSelection = null; SelectedDocumentNode = null;
                _navigationSession = snapshot.SessionId;
            }
            var old = SelectedDocumentNode;
            var expanded = Descendants(DocumentNavigationItems).Where(n => n.IsExpanded).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var projection = _workspaceNavigator.Build(snapshot!, source, NavigationLayout);
            if (_documentSelection is not null)
            {
                _documentSelection = projection.RetainSelection(_documentSelection).ToHashSet();
                if (_documentSelection.Count == 0) _documentSelection = null;
            }
            DocumentNavigationItems = new(projection.Roots.Select(n => new WorkspaceNavigationItemViewModel(n)));
            var nodes = Descendants(DocumentNavigationItems).ToArray();
            foreach (var node in nodes) node.IsExpanded = expanded.Contains(node.Id);
            SelectedDocumentNode = old is null ? null : nodes.FirstOrDefault(n => n.Id == old.Id)
                ?? nodes.FirstOrDefault(n => n.Node.Kind == old.Node.Kind && _documentSelection is not null && _documentSelection.SetEquals(n.Node.EntryIds));
        }
        finally { _refreshingNavigation = false; }
    }

    private static IEnumerable<WorkspaceNavigationItemViewModel> Descendants(IEnumerable<WorkspaceNavigationItemViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Descendants(root.Children)) yield return child;
        }
    }

    private Guid? ResolveDocumentEntry(string key)
    {
        if (SelectedGroup is { EntryId: { } id } group && group.Namespace == key) return id;
        var ids = AllTranslation.Where(t => t.Namespace == key).Select(Workspace.GetProjectionEntryId).Distinct().ToArray();
        if (ids.Length == 1) return ids[0];
        StatusText = "Select the entry in the navigator to distinguish its source document.";
        return null;
    }

    private IEnumerable<TranslationItem> ItemsForEditorKey(string key)
    {
        var items = AllTranslation.Where(t => t.Namespace == key);
        if (!HasDocumentNavigation) return items;
        var id = ResolveDocumentEntry(key);
        return id is null ? [] : items.Where(t => Workspace.GetProjectionEntryId(t) == id);
    }
}
