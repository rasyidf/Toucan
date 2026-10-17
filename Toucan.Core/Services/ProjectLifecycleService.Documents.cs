using Toucan.Core.Contracts.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public partial class ProjectLifecycleService
{
    private async Task<ProjectSaveResult> SaveDocumentProjectAsync(SaveOptions options, CancellationToken ct)
    {
        if (workspace?.Source is not { } source || documentSaves is null || _currentProject is null)
            return new(ProjectSaveStatus.FileSystemError, ErrorMessage: "Document persistence is unavailable.");
        var findings = workspace.Validate();
        if (options.EnforceValidation && findings.Any(f => f.Severity == ValidationSeverity.Error))
            return new(ProjectSaveStatus.ValidationErrors, Errors: findings.Where(f => f.Severity == ValidationSeverity.Error).ToArray(), Findings: findings);
        try
        {
            var commit = workspace.CommitProjection(translationManagement.Translations);
            if (!commit.Applied) return new(ProjectSaveStatus.FileSystemError, ErrorMessage: commit.Issues[0].Message);
            FlushRecoveryDraft();
            var result = await documentSaves.SaveDispatchedAsync(source, DispatchToUiAsync, ct).ConfigureAwait(false);
            if (!result.Acknowledged) return new(ProjectSaveStatus.Cancelled, ErrorMessage: "The workspace session changed during save.");
            await DispatchToUiAsync(() =>
            {
                if (workspace.SessionId != result.Receipt.Plan.SessionId) return Task.CompletedTask;
                _currentProject.IsDirty = workspace.IsDirty;
                if (!workspace.IsDirty) DeleteRecoveryDraft(); else FlushRecoveryDraft();
                var saved = result.Receipt.Source;
                var writes = result.Receipt.Plan.Targets.Select(target => new FileVersionChange(target.RelativePath, target.ExpectedVersion,
                    saved.Documents.SingleOrDefault(d => d.RelativePath == target.RelativePath)?.Version))
                    .Concat(result.Receipt.Plan.Artifacts.Select(target => new FileVersionChange(target.RelativePath, target.ExpectedVersion,
                        saved.Artifacts.SingleOrDefault(a => a.RelativePath == target.RelativePath)?.Version))).ToArray();
                fileWatcher.AcknowledgeWrites(source.Root, writes);
                UpdateLastSavedSnapshot(); autoSave.ResetTimer();
                ProjectChanged?.Invoke(this, new() { ProjectPath = source.Root, ChangeType = ProjectChangeType.Saved });
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            return new(ProjectSaveStatus.Success, Findings: findings);
        }
        catch (WorkspaceSourceConflictException ex)
        { return new(options.OverwriteExternalChanges ? ProjectSaveStatus.FileSystemError : ProjectSaveStatus.ExternalChanges, ErrorMessage: ex.Message + " Reload or merge external changes before saving.", Findings: findings, ExternalFiles: [ex.RelativePath ?? source.Root]); }
        catch (OperationCanceledException) { return new(ProjectSaveStatus.Cancelled); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        { return new(ProjectSaveStatus.FileSystemError, ErrorMessage: ex.Message, Findings: findings); }
    }
    private async Task<ProjectSaveResult> SaveDocumentProjectAsAsync(string targetFolder, CancellationToken ct)
    {
        if (workspace?.Source is not { } source || exportPlanner is null || documentExecutor is null || _currentProject is null)
            return new(ProjectSaveStatus.FileSystemError, ErrorMessage: "Document export is unavailable.");
        try
        {
            workspace.CommitProjection(translationManagement.Translations);
            var oldSettings = _currentProject;
            var snapshot = workspace.CaptureDocumentSnapshot()!;
            var plan = exportPlanner.Plan(source, snapshot, targetFolder, ct);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, workspace.ExportSessionToken(snapshot));
            var receipt = await documentExecutor.ExecuteAsync(plan, lifetime.Token).ConfigureAwait(false);
            var switched = false;
            await DispatchToUiAsync(async () =>
            {
                if (workspace.SessionId != snapshot.SessionId) return;
                var current = workspace.CaptureRecovery(source);
                var baseline = receipt.Source.Units.Select(u => new WorkspaceDocumentUnit(u.EntryId, u.Language, u.Namespace, u.KeyPath, u.Value, u.Binding, u.RawScalar, u.Metadata)).ToArray();
                var saved = baseline.ToDictionary(u => (u.EntryId, u.Language));
                var desired = current.Units.Select(u => saved.TryGetValue((u.EntryId, u.Language), out var written)
                    ? u with { Binding = written.Binding, RawScalar = u.Value == written.Value ? written.RawScalar : u.RawScalar } : u).ToArray();
                var autoSaving = autoSave.IsEnabled;
                workspace.BeginClosing(); autoSave.Stop(); StopDraftTimer(); UnsubscribeFromFileWatcher(); fileWatcher.Stop();
                if (pluginActivation is not null) await pluginActivation.CloseWorkspaceAsync(oldSettings.ProjectPath);
                workspace.Close();
                _currentProject = oldSettings.CloneForRoot(receipt.Source.Root);
                languageManagement.SetProjectSettings(_currentProject);
                workspace.Open(_currentProject, receipt.Source);
                workspace.ApplyRecovery(new(receipt.Source.Root, receipt.Source.FrameworkId, receipt.Source.FormatId, baseline, desired,
                    new(receipt.Source.Root, receipt.Source.IdentityCatalog)));
                recoveryDrafts?.Delete(oldSettings.ProjectPath); _pendingRecovery = null; _interruptedSave = null;
                fileWatcher.Watch(receipt.Source.Root); SubscribeToFileWatcher();
                recentProjects.Add(receipt.Source.Root); recentProjects.Save(); UpdateLastSavedSnapshot(); StartDraftTimer();
                if (pluginActivation is not null) await pluginActivation.OpenWorkspaceAsync(receipt.Source.Root, CancellationToken.None);
                if (autoSaving) autoSave.Start(TimeSpan.FromSeconds(Math.Clamp(_currentProject.AutoSaveIntervalSeconds ?? 60, 10, 600)));
                switched = true;
                ProjectChanged?.Invoke(this, new() { ProjectPath = receipt.Source.Root, ChangeType = ProjectChangeType.Saved });
            }).ConfigureAwait(false);
            return new(switched ? ProjectSaveStatus.Success : ProjectSaveStatus.Cancelled);
        }
        catch (OperationCanceledException) { return new(ProjectSaveStatus.Cancelled); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        { return new(ProjectSaveStatus.FileSystemError, ErrorMessage: ex.Message); }
    }
}
