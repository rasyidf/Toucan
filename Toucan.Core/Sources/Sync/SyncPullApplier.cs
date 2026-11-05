using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>What applying the pull side of a plan did.</summary>
/// <param name="Skipped">Changes refused because the project's value moved on after the plan's snapshot. Plan again to see them.</param>
public sealed record PullResult(int Applied, int Created, int Deleted, IReadOnlyList<PullChange> Skipped, string? Refusal, long Revision)
{
    public bool Succeeded => Refusal is null;
}

/// <summary>Maps between the project's review states and a service's review meanings.</summary>
public static class SyncMapping
{
    public static SourceReviewMeaning ToMeaning(ReviewState state, string value) => state switch
    {
        ReviewState.Approved => SourceReviewMeaning.Approved,
        ReviewState.NeedsReview => SourceReviewMeaning.NeedsReview,
        _ => string.IsNullOrEmpty(value) ? SourceReviewMeaning.Untranslated : SourceReviewMeaning.Draft,
    };

    public static ReviewState ToReview(SourceReviewMeaning meaning) => meaning switch
    {
        SourceReviewMeaning.Approved or SourceReviewMeaning.Reviewed => ReviewState.Approved,
        SourceReviewMeaning.NeedsReview => ReviewState.NeedsReview,
        _ => ReviewState.Draft,
    };

    public static IReadOnlyList<SyncLocalUnit> LocalUnits(IdentifiedWorkspaceSnapshot snapshot, IReadOnlyCollection<string>? onlyLanguages = null) =>
        [.. snapshot.Units.Where(u => onlyLanguages is null || onlyLanguages.Contains(u.Language))
            .Select(u => new SyncLocalUnit(u.EntryId, u.Language, u.Namespace, u.KeyPath, u.Value, u.Comment, ToMeaning(u.Review, u.Value)))];
}

/// <summary>
/// Applies a plan's pull changes through <see cref="WorkspaceService"/> as one transaction with origin <c>Source</c>. A value
/// edited since the plan's snapshot is skipped, never overwritten. The baseline moves only for what was applied.
/// </summary>
public sealed class SyncPullApplier(WorkspaceService workspace, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<PullResult> ApplyAsync(SyncPlan plan, SourceConnectionSettings connection, SyncStateStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(store);
        var snapshot = await workspace.SnapshotEntriesAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No project is open.");
        var current = snapshot.Units.ToDictionary(u => (u.EntryId, u.Language));
        var baseline = store.LoadBaseline() ?? new SyncBaseline();
        var now = _time.GetUtcNow();

        var included = plan.Pull.Where(c => c.Include).ToList();
        var skipped = new List<PullChange>();
        var operations = new List<EntryOperation>();
        var statusOperations = new List<(EntryOperation Operation, PullChange Change)>();
        var accepted = new List<PullChange>();
        var deleted = new HashSet<Guid>();
        foreach (var change in included.Where(c => c.Kind is PullKind.Update or PullKind.Delete or PullKind.Rename))
        {
            if (change.EntryId is not { } entry || !current.TryGetValue((entry, change.Language), out var unit) ||
                change.LocalValue is not null && unit.Value != change.LocalValue)
            {
                skipped.Add(change);
                continue;
            }
            switch (change.Kind, change.Field)
            {
                case (PullKind.Delete, _):
                    if (deleted.Add(entry)) operations.Add(new DeleteEntry(entry));
                    break;
                case (PullKind.Rename, _):
                    operations.Add(new RenameEntry(entry, unit.Namespace, change.KeyPath));
                    break;
                case (_, SyncField.Value):
                    operations.Add(new SetEntryValue(entry, change.Language, change.NewValue ?? string.Empty, change.LocalValue));
                    break;
                case (_, SyncField.Status):
                    // Applied apart from the values: the approval policy may refuse an approval, and that must not undo the rest.
                    if (change.NewStatus is { } status) statusOperations.Add((new SetEntryReview(entry, change.Language, SyncMapping.ToReview(status)), change));
                    continue;
                case (_, SyncField.Comment):
                    operations.Add(new SetEntryComment(entry, change.Language, change.NewComment ?? string.Empty));
                    break;
            }
            accepted.Add(change);
        }

        var revision = snapshot.Revision;
        if (operations.Count > 0)
        {
            var label = $"Pulled from {connection.Name ?? connection.Id}";
            var result = workspace.ApplySourceEntries(connection.Id, new EntryEdit(snapshot.SessionId, snapshot.Revision, operations, label), cancellationToken);
            if (!result.Applied)
                return new(0, 0, 0, [.. skipped, .. accepted], string.Join(" ", result.Issues.Select(i => i.Message)), result.Revision);
            revision = result.Revision;
        }

        if (statusOperations.Count > 0)
        {
            var latest = await workspace.SnapshotEntriesAsync(cancellationToken).ConfigureAwait(false);
            var review = workspace.ApplySourceEntries(connection.Id, new EntryEdit(latest!.SessionId, latest.Revision, [.. statusOperations.Select(o => o.Operation)], $"Pulled review states from {connection.Name ?? connection.Id}"), cancellationToken);
            if (review.Applied) { accepted.AddRange(statusOperations.Select(o => o.Change)); revision = review.Revision; }
            else skipped.AddRange(statusOperations.Select(o => o.Change));
        }

        // New keys are added in a second step; they cannot be expressed as identified edits.
        var createdKeys = new List<(string Key, PullChange Sample, IReadOnlyDictionary<string, string> Values)>();
        foreach (var group in included.Where(c => c.Kind == PullKind.Create).GroupBy(c => (c.Remote.ResourceId, c.Remote.Name)))
        {
            var first = group.First();
            var key = string.Join('.', first.KeyPath.Segments);
            if (snapshot.Units.Any(u => u.Key == key)) { skipped.AddRange(group); continue; }
            createdKeys.Add((key, first, first.NewValues ?? group.ToDictionary(c => c.Language, c => c.NewValue ?? string.Empty)));
        }
        var created = 0;
        if (createdKeys.Count > 0)
        {
            var edit = new WorkspaceEdit { Label = $"Pulled from {connection.Name ?? connection.Id}" };
            foreach (var (key, _, values) in createdKeys) edit.AddKey(key, values);
            var result = workspace.ApplySource(connection.Id, edit, snapshot.SessionId, cancellationToken);
            if (!result.Applied)
            {
                SaveBaseline(store, baseline, plan, accepted, [], now);
                return new(accepted.Count, 0, deleted.Count, skipped, string.Join(" ", result.Issues.Select(i => i.Message)), result.Revision);
            }
            revision = result.Revision;
            created = createdKeys.Count;
        }

        var after = created > 0 ? await workspace.SnapshotEntriesAsync(cancellationToken).ConfigureAwait(false) : snapshot;
        SaveBaseline(store, baseline, plan, [.. accepted, .. included.Where(c => c.Kind == PullKind.Converge)],
            createdKeys.SelectMany(k => k.Values.Select(v => (k.Key, k.Sample, v.Key, v.Value))).ToList(), now, after);
        return new(accepted.Count(c => c.Kind != PullKind.Delete), created, deleted.Count, skipped, null, revision);
    }

    private static void SaveBaseline(SyncStateStore store, SyncBaseline baseline, SyncPlan plan, IReadOnlyList<PullChange> applied,
        IReadOnlyList<(string Key, PullChange Sample, string Language, string Value)> createdValues, DateTimeOffset now, IdentifiedWorkspaceSnapshot? after = null)
    {
        var byUnit = baseline.Units.ToDictionary(u => (u.EntryId, u.Language));
        foreach (var change in applied)
        {
            if (change.EntryId is not { } entry) continue;
            if (change.Kind == PullKind.Delete) { byUnit.Remove((entry, change.Language)); continue; }
            var unit = byUnit.GetValueOrDefault((entry, change.Language)) ?? new BaselineUnit { EntryId = entry, Language = change.Language };
            unit.Remote = change.Remote;
            unit.RemoteRevision = change.RemoteRevision ?? unit.RemoteRevision;
            if (change.Field == SyncField.Value && change.NewValue is { } value || change.Kind == PullKind.Converge)
            {
                unit.Value = change.NewValue ?? unit.Value;
                unit.ValueHash = BaselineUnit.Hash(unit.Value);
            }
            if (change.NewStatus is { } status) unit.Status = status;
            if (change.NewComment is { } comment) unit.CommentHash = BaselineUnit.Hash(comment);
            unit.SyncedAt = now;
            byUnit[(entry, change.Language)] = unit;
        }
        if (after is not null)
        {
            foreach (var (key, sample, language, value) in createdValues)
            {
                var found = after.Units.FirstOrDefault(u => u.Key == key && u.Language == language);
                if (found is null) continue;
                byUnit[(found.EntryId, language)] = new BaselineUnit
                {
                    EntryId = found.EntryId, Language = language, Remote = sample.Remote, RemoteRevision = sample.RemoteRevision,
                    Value = value, ValueHash = BaselineUnit.Hash(value), Status = sample.NewStatus, SyncedAt = now,
                };
            }
        }
        foreach (var (entry, language) in plan.Forget) byUnit.Remove((entry, language));
        baseline.Units = [.. byUnit.Values];
        baseline.LastPull = now;
        store.SaveBaseline(baseline);
    }
}
