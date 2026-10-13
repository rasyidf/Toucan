using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Plugins = Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// What plugins may do to the open project (<see cref="Plugins.IWorkspaceApi"/>). Edits are planned first on a copy
/// (<see cref="WorkspaceEditPlanner"/>) and then applied here with the same primitives as the editor's own bulk operations:
/// an undo record per value (one step for the whole edit), the translation store for dirty tracking, <c>MarkStructureChanged</c>
/// for keys, the strict-approval policy for approvals. Autosave and crash recovery see the result like any other edit.
/// </summary>
public partial class MainWindowViewModel
{
    private long _workspaceRevision = 1;

    /// <summary>Raised on the UI thread when the project opens or closes or anything in it changes.</summary>
    internal event EventHandler? WorkspaceChanged;

    /// <summary>Marks that the project moved on, so an edit prepared from an older snapshot can be refused.</summary>
    private void BumpWorkspaceRevision()
    {
        _workspaceRevision++;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool IsWorkspaceOpen => HasProject;

    internal Plugins.WorkspaceSnapshot? BuildWorkspaceSnapshot()
    {
        if (!HasProject) return null;
        FlushPendingEdits(); // include what the user has typed but not yet committed

        var units = AllTranslation.Select(t => new Plugins.UnitSnapshot(t.Namespace, t.Language, t.Value ?? string.Empty, t.Comment ?? string.Empty,
            t.IsApproved ? Plugins.ReviewState.Approved : Plugins.ReviewState.Draft, _translationStore.IsItemDirty(t))).ToList();
        return new Plugins.WorkspaceSnapshot(CurrentPath, CurrentPath, PrimaryLanguage, ProjectLanguages(), units, _workspaceRevision);
    }

    internal Plugins.EditResult ApplyWorkspaceEdit(string pluginId, Plugins.WorkspaceEdit edit)
    {
        if (!HasProject)
            return Plugins.EditResult.Refused(_workspaceRevision, new Plugins.EditIssue(-1, Plugins.EditIssueKind.NoProject, "No project is open."));

        FlushPendingEdits();
        var plan = WorkspaceEditPlanner.Plan(new PlanInput(AllTranslation, ProjectLanguages(), PrimaryLanguage, _workspaceRevision,
            ProjectSettings?.RequireValidForApproval == true, _validationPipeline), edit);
        if (!plan.CanApply) return new Plugins.EditResult(false, 0, _workspaceRevision, plan.Issues, []);
        if (plan.Steps.Count == 0) return new Plugins.EditResult(true, 0, _workspaceRevision, plan.Issues, []);

        var changed = new List<TranslationItem>();
        var structure = new List<string>();
        var touched = new HashSet<(string, string)>();
        var touchedKeys = new HashSet<string>();
        var now = DateTime.UtcNow;

        // One undo step for every value in the edit, like a bulk operation in the editor should be.
        using (_undoRedoService.BeginGroup())
        {
            foreach (var (_, operation) in plan.Steps)
            {
                switch (operation)
                {
                    case Plugins.SetValue set:
                    {
                        var item = Find(set.Key, set.Language);
                        _undoRedoService.Record(item.Namespace, item.Language, item.Value, set.Value ?? string.Empty);
                        item.Value = set.Value ?? string.Empty;
                        item.ChangeType = ChangeType.External;
                        item.LastModifiedUtc = now;
                        changed.Add(item);
                        touched.Add((item.Namespace, item.Language));
                        break;
                    }

                    case Plugins.SetComment comment:
                    {
                        var item = Find(comment.Key, comment.Language);
                        _translationStore.NotifyCommentChanged(item, comment.Comment ?? string.Empty);
                        item.LastModifiedUtc = now;
                        changed.Add(item);
                        touched.Add((item.Namespace, item.Language));
                        break;
                    }

                    case Plugins.SetReview review:
                    {
                        var item = Find(review.Key, review.Language);
                        item.IsApproved = review.State == Plugins.ReviewState.Approved;
                        item.ApprovedAtUtc = item.IsApproved ? now : null;
                        changed.Add(item);
                        touched.Add((item.Namespace, item.Language));
                        break;
                    }

                    case Plugins.AddKey add:
                        foreach (var language in ProjectLanguages())
                        {
                            var value = add.Values?.FirstOrDefault(v => string.Equals(v.Key, language, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
                            var item = new TranslationItem { Namespace = add.Key, Language = language, Value = value, LastModifiedUtc = now, ChangeType = ChangeType.External };
                            AllTranslation.Add(item);
                            touched.Add((item.Namespace, item.Language));
                        }
                        structure.Add(add.Key);
                        break;

                    case Plugins.RenameKey rename:
                        foreach (var item in AllTranslation.Where(t => MatchesNode(t.Namespace, rename.Key)).ToList())
                        {
                            item.Namespace = item.Namespace == rename.Key ? rename.NewKey : rename.NewKey + item.Namespace[rename.Key.Length..];
                            touched.Add((item.Namespace, item.Language));
                            structure.Add(item.Namespace);
                        }
                        break;

                    case Plugins.DeleteKey delete:
                    {
                        var removed = AllTranslation.Where(t => MatchesNode(t.Namespace, delete.Key)).ToList();
                        AllTranslation.RemoveAll(t => MatchesNode(t.Namespace, delete.Key));
                        structure.AddRange(removed.Select(t => t.Namespace).Distinct());
                        if (SelectedNode is { } node && MatchesNode(node.Namespace, delete.Key)) SelectedNode = null;
                        if (SelectedGroup is { } group && MatchesNode(group.Namespace, delete.Key)) SelectedGroup = null;
                        break;
                    }
                }
            }
        }

        if (changed.Count > 0) NotifyBulkValueChanges(changed);
        if (structure.Count > 0)
        {
            MarkStructureChanged(structure);
            RefreshTree();
        }
        var shown = changed.ToHashSet();
        foreach (var vm in PagingController.PageData.SelectMany(g => g.AllItems).Where(v => shown.Contains(v.Model))) vm.Refresh();
        UpdateSummaryInfo();

        // What the edit left behind, for the plugin and for the Issues panel (without pulling it forward). Never blocks anything.
        touchedKeys.UnionWith(touched.Select(t => t.Item1));
        var results = _validationPipeline.RunAll(new ValidationContext { Items = AllTranslation, PrimaryLanguage = PrimaryLanguage }).ToList();
        ShowValidationResults(results);
        var findings = WorkspaceEditPlanner.Findings(results, touched, touchedKeys);

        BumpWorkspaceRevision();
        StatusText = edit.Label is { Length: > 0 } label ? $"{label}: {plan.ChangedUnits} change(s)" : $"{pluginId}: {plan.ChangedUnits} change(s)";
        return new Plugins.EditResult(true, plan.ChangedUnits, _workspaceRevision, plan.Issues, findings);

        TranslationItem Find(string key, string language) => AllTranslation.First(t => t.Namespace == key && t.Language == language);
    }
}
