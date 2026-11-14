using Toucan.Core.Contracts.Services;
using Plugins = Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Desktop projection of the Core workspace. Pending text and presentation stay here.</summary>
public partial class MainWindowViewModel
{
    private long _workspaceRevision = 1;

    private void BumpWorkspaceRevision()
    {
        _workspaceRevision++;
        if (!HasProject || Workspace.ProjectPath != CurrentPath) return;
        var result = Workspace.CommitProjection(AllTranslation);
        if (!result.Applied && Workspace.IsOpen)
            RefreshWorkspaceProjection(true, result.Issues[0].Message);
    }

    internal bool IsWorkspaceOpen => Workspace.IsOpen;

    internal Plugins.WorkspaceSnapshot? BuildWorkspaceSnapshot()
    {
        FlushPendingEdits();
        return Workspace.SnapshotAsync().GetAwaiter().GetResult();
    }

    internal Plugins.EditResult ApplyWorkspaceEdit(string pluginId, Plugins.WorkspaceEdit edit, Guid? sessionId = null, CancellationToken cancellationToken = default)
    {
        FlushPendingEdits();
        var result = Workspace.Apply(pluginId, edit, sessionId, cancellationToken);
        if (result.Applied && result.ChangedUnits > 0)
            RefreshWorkspaceProjection(edit.Operations.Any(op => op is Plugins.AddKey or Plugins.RenameKey or Plugins.DeleteKey),
                edit.Label is { Length: > 0 } label ? label : pluginId, result.ChangedUnits);
        return result;
    }

    internal void RefreshWorkspaceProjection(bool structureChanged, string label, int changedUnits = 0)
    {
        if (!HasProject || Workspace.SnapshotAsync().GetAwaiter().GetResult()?.ProjectPath != CurrentPath) return;
        // Refresh existing rows before replacing structure, so references held by a focused control stay coherent.
        foreach (var vm in PagingController.PageData.SelectMany(g => g.AllItems)) vm.Refresh();
        AllTranslation = _translationStore.Translations.ToList();
        foreach (var item in AllTranslation.Where(_translationStore.IsItemDirty)) SessionDirtyKeys.Add(item.Namespace);
        SessionDirtyCount = SessionDirtyKeys.Count;
        _hasUntrackedChanges = Workspace.IsDirty;
        IsDirty = Workspace.IsDirty;
        if (structureChanged)
        {
            if (SelectedNode is { } node && !AllTranslation.Any(t => MatchesNode(t.Namespace, node.Namespace))) SelectedNode = null;
            if (!HasDocumentNavigation && SelectedGroup is { } group && !AllTranslation.Any(t => MatchesNode(t.Namespace, group.Namespace))) SelectedGroup = null;
            RefreshTree();
            Search(SearchText, true);
        }
        MarkDirtyGroups();
        UpdateSummaryInfo();
        ShowValidationResults(Workspace.Validate());
        StatusText = changedUnits > 0 ? $"{label}: {changedUnits} change(s)" : label;
    }
}
