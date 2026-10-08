using System.IO;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Crash and failure handling for the project lifecycle: external-edit detection at save time, recovery drafts of
/// unsaved edits, and resolution of saves that were interrupted.
/// </summary>
public partial class ProjectLifecycleService
{
    private static readonly TimeSpan DraftInterval = TimeSpan.FromSeconds(5);

    private Timer? _draftTimer;
    private string _lastDraftSignature = string.Empty;
    private Dictionary<(string Language, string Namespace), TranslationItem> _snapshotIndex = [];
    private Dictionary<string, string> _diskBaseline = [];
    private RecoveryDraft? _pendingRecovery;
    private SaveTransaction? _interruptedSave;

    /// <inheritdoc />
    public RecoveryDraft? PendingRecovery => _pendingRecovery;

    /// <inheritdoc />
    public bool HasInterruptedSave => _interruptedSave != null;

    // ───────────────────────── Files a save touches ─────────────────────────

    /// <summary>Every file a save may write or delete: language files, comment sidecars, the audit sidecar and the manifest.</summary>
    private List<string> TrackedFiles(ProjectSettings settings, bool includeManifest = true)
    {
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings.LanguageAliases is { Count: > 0 })
            foreach (var kv in settings.LanguageAliases) reverse[kv.Value] = kv.Key;

        var displayLanguages = translationManagement.Translations.Select(t => t.Language)
            .Concat(settings.Languages).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal).ToList();
        var fileLanguages = displayLanguages.Select(l => reverse.TryGetValue(l, out var code) ? code : l).Distinct(StringComparer.Ordinal).ToList();

        var files = new List<string>();
        foreach (var language in fileLanguages)
            files.AddRange(projectService.GetLanguageFiles(settings, language));
        files.AddRange(commentPersistence.GetSidecarPaths(settings.ProjectPath, settings.SaveFormat, fileLanguages));
        files.Add(Path.Combine(settings.ProjectPath, ".toucan-metadata.json"));
        if (includeManifest) files.Add(Path.Combine(settings.ProjectPath, "toucan.tproj"));
        return files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Remembers what the project files look like now. Called whenever Toucan's in-memory state is known to match disk.
    /// The manifest is left out: Toucan rewrites it from several places outside Save.
    /// </summary>
    private void CaptureDiskBaseline()
    {
        try
        {
            _diskBaseline = _currentProject == null
                ? []
                : SaveTransaction.Fingerprint(TrackedFiles(_currentProject, includeManifest: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not fingerprint project files; external-edit detection is off until the next save");
            _diskBaseline = [];
        }
    }

    /// <summary>Files that existed when Toucan last synced with disk and have been edited or removed since.</summary>
    private List<string> ExternallyChangedFiles(ProjectSettings settings)
    {
        var current = SaveTransaction.Fingerprint(TrackedFiles(settings, includeManifest: false));
        return current
            .Where(kv => _diskBaseline.TryGetValue(kv.Key, out var known) && known.Length > 0 && known != kv.Value)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // ───────────────────────── Recovery drafts ─────────────────────────

    private void StartDraftTimer() =>
        _draftTimer = new Timer(_ => FlushRecoveryDraft(), null, DraftInterval, DraftInterval);

    private void StopDraftTimer()
    {
        _draftTimer?.Dispose();
        _draftTimer = null;
        _lastDraftSignature = string.Empty;
    }

    /// <summary>
    /// Writes the unsaved edits to a recovery draft (or removes the draft once nothing is unsaved). Runs on a timer;
    /// public so a caller can force it, for instance right before a risky operation.
    /// </summary>
    public void FlushRecoveryDraft()
    {
        if (recoveryDrafts is null || _currentProject is null) return;
        // An undecided draft from an earlier session is the only copy of that work: leave it alone.
        if (_pendingRecovery != null) return;

        try
        {
            var path = _currentProject.ProjectPath;
            var draft = BuildDraft();
            if (draft.ChangeCount == 0)
            {
                if (_lastDraftSignature.Length > 0) recoveryDrafts.Delete(path);
                _lastDraftSignature = string.Empty;
                return;
            }

            var signature = string.Join('\u001f',
                draft.Entries.Select(e => $"e{e.Language}\u001e{e.Namespace}\u001e{e.Value}\u001e{e.Comment}")
                    .Concat(draft.Deleted!.Select(d => $"d{d.Language}\u001e{d.Namespace}"))
                    .Concat(draft.Approvals!.Select(a => $"a{a.Language}\u001e{a.Namespace}\u001e{a.Approved}")));
            if (signature == _lastDraftSignature) return;
            recoveryDrafts.Write(path, draft);
            _lastDraftSignature = signature;
        }
        catch (Exception ex)
        {
            // Never let the safety net take the app down (this runs on a timer thread).
            logger.LogWarning(ex, "Could not update the recovery draft");
        }
    }

    /// <summary>
    /// Everything that differs from the last load or save: edited and added values (from the dirty tracker), and the
    /// deleted keys and approval changes the dirty tracker cannot see (from the last-saved snapshot).
    /// </summary>
    private RecoveryDraft BuildDraft()
    {
        var entries = new List<RecoveryDraftEntry>();
        foreach (var item in translationManagement.GetDirtyItems())
        {
            var hasBase = translationManagement.TryGetSavedState(item, out var savedValue, out var savedComment);
            entries.Add(new RecoveryDraftEntry(item.Language, item.Namespace,
                hasBase ? savedValue : null, item.Value ?? string.Empty,
                hasBase ? savedComment : null, item.Comment ?? string.Empty));
        }

        var current = translationManagement.Translations.ToArray();
        var currentKeys = new HashSet<(string, string)>(current.Select(t => (t.Language, t.Namespace)));
        var deleted = _snapshotIndex.Values
            .Where(t => !currentKeys.Contains((t.Language, t.Namespace)))
            .Select(t => new RecoveryDraftDeletion(t.Language, t.Namespace, t.Value ?? string.Empty))
            .ToList();
        var approvals = new List<RecoveryDraftApproval>();
        foreach (var item in current)
            if (_snapshotIndex.TryGetValue((item.Language, item.Namespace), out var saved) && saved.IsApproved != item.IsApproved)
                approvals.Add(new RecoveryDraftApproval(item.Language, item.Namespace, item.IsApproved));

        return new RecoveryDraft(_currentProject!.ProjectPath, DateTime.UtcNow, entries, deleted, approvals);
    }

    private void DeleteRecoveryDraft()
    {
        if (_currentProject != null) recoveryDrafts?.Delete(_currentProject.ProjectPath);
        _lastDraftSignature = string.Empty;
    }

    /// <inheritdoc />
    public RecoveryApplyResult ApplyRecovery()
    {
        var draft = _pendingRecovery;
        if (draft is null || _currentProject is null) return new RecoveryApplyResult(0, []);
        _pendingRecovery = null;

        var byKey = translationManagement.Translations
            .GroupBy(t => (t.Language, t.Namespace)).ToDictionary(g => g.Key, g => g.First());
        var applied = 0;
        var conflicts = new List<RecoveryDraftEntry>();
        var added = new List<TranslationItem>();

        foreach (var entry in draft.Entries)
        {
            byKey.TryGetValue((entry.Language, entry.Namespace), out var item);
            if (item is null)
            {
                // Still absent: a new entry can be re-created; an edit to something deleted on disk is a conflict.
                if (entry.BaseValue is null)
                {
                    added.Add(new TranslationItem { Language = entry.Language, Namespace = entry.Namespace, Value = entry.Value, Comment = entry.Comment ?? string.Empty });
                    applied++;
                }
                else conflicts.Add(entry);
                continue;
            }

            var valueChanged = entry.Value != entry.BaseValue;
            var commentChanged = (entry.Comment ?? string.Empty) != (entry.BaseComment ?? string.Empty);
            var conflict = false;
            var did = false;

            if (valueChanged)
            {
                // Disk still holds what the edit started from (or already holds the edited text): safe to apply.
                if (item.Value == entry.Value) { }
                else if (item.Value == entry.BaseValue) { translationManagement.NotifyValueChanged(item, entry.Value); did = true; }
                else conflict = true;
            }
            if (commentChanged)
            {
                var diskComment = item.Comment ?? string.Empty;
                if (diskComment == (entry.Comment ?? string.Empty)) { }
                else if (diskComment == (entry.BaseComment ?? string.Empty)) { translationManagement.NotifyCommentChanged(item, entry.Comment ?? string.Empty); did = true; }
                else conflict = true;
            }

            if (did) applied++;
            if (conflict) conflicts.Add(entry);
        }

        if (added.Count > 0) translationManagement.AddItems(added);

        var removals = new HashSet<(string, string)>();
        foreach (var d in draft.Deleted ?? [])
        {
            if (!byKey.TryGetValue((d.Language, d.Namespace), out var existing)) continue; // already gone on disk
            if (existing.Value == d.BaseValue) { removals.Add((d.Language, d.Namespace)); applied++; }
            else conflicts.Add(new RecoveryDraftEntry(d.Language, d.Namespace, d.BaseValue, string.Empty, null, null));
        }
        if (removals.Count > 0) translationManagement.RemoveItems(t => removals.Contains((t.Language, t.Namespace)));

        foreach (var a in draft.Approvals ?? [])
        {
            if (!byKey.TryGetValue((a.Language, a.Namespace), out var existing) || existing.IsApproved == a.Approved) continue;
            existing.IsApproved = a.Approved;
            existing.ApprovedAtUtc = a.Approved ? DateTime.UtcNow : null;
            applied++;
        }

        // The edits are in memory again; the timer rewrites the draft from the dirty state.
        recoveryDrafts?.Delete(_currentProject.ProjectPath);
        _lastDraftSignature = string.Empty;
        if (conflicts.Count > 0 && logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning("Recovery skipped {Count} edit(s) because the files changed on disk since the draft was written", conflicts.Count);
        return new RecoveryApplyResult(applied, conflicts);
    }

    /// <inheritdoc />
    public void DiscardRecovery()
    {
        if (_pendingRecovery is null) return;
        _pendingRecovery = null;
        DeleteRecoveryDraft();
    }

    // ───────────────────────── Interrupted saves ─────────────────────────

    /// <inheritdoc />
    public async Task<SaveRollbackResult> RestoreInterruptedSaveAsync()
    {
        var journal = _interruptedSave;
        if (journal is null || _currentProject is null) return new SaveRollbackResult([], []);

        var result = journal.Rollback();
        if (result.Complete) _interruptedSave = null;
        await DispatchToUiAsync(ReloadFromDiskAsync).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public void KeepInterruptedSave()
    {
        _interruptedSave?.Discard();
        _interruptedSave = null;
    }

    /// <summary>Looks for leftovers of an earlier session when a project opens.</summary>
    private void DetectLeftovers(string folderPath)
    {
        _interruptedSave = SaveTransaction.FindInterrupted(folderPath);
        _pendingRecovery = recoveryDrafts?.TryRead(folderPath);
    }
}
