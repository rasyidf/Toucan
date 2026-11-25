using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

public enum SyncPlanKind
{
    Pull,
    Push,
    Sync,
}

public enum SyncField
{
    Value,
    Status,
    Comment,
    Existence,
    Name,
}

public enum PullKind
{
    /// <summary>A field of a bound translation changed at the service.</summary>
    Update,

    /// <summary>A key that exists only at the service. It is added to the project.</summary>
    Create,

    /// <summary>The service deleted a key. Never included unless the user includes it.</summary>
    Delete,

    /// <summary>The service renamed a key.</summary>
    Rename,

    /// <summary>Both sides already agree; only the baseline moves.</summary>
    Converge,
}

public enum PushKind
{
    Value,
    Status,
    Comment,
    Create,
    Rename,
    DeleteKey,
    DeleteTranslation,
}

/// <summary>One change coming into the project.</summary>
public sealed record PullChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required PullKind Kind { get; init; }
    public required SyncField Field { get; init; }

    /// <summary>The project entry, or null for a key the project does not have yet.</summary>
    public Guid? EntryId { get; init; }

    /// <summary>The project language.</summary>
    public required string Language { get; init; }
    public required SyncRemoteKey Remote { get; init; }
    public TranslationPath Namespace { get; init; } = TranslationPath.Empty;
    public TranslationPath KeyPath { get; init; } = TranslationPath.Empty;

    /// <summary>The project's value when the plan was made; the pull is refused if the unit moved on.</summary>
    public string? LocalValue { get; init; }
    public string? NewValue { get; init; }
    public SourceReviewMeaning? NewStatus { get; init; }
    public string? NewComment { get; init; }
    public string? RemoteRevision { get; init; }

    /// <summary>For a created key: project language → value.</summary>
    public IReadOnlyDictionary<string, string>? NewValues { get; init; }

    public bool Include { get; init; } = true;
}

/// <summary>One change going out to the service.</summary>
public sealed record PushChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required PushKind Kind { get; init; }
    public Guid EntryId { get; init; }
    public required string Language { get; init; }
    public required SyncRemoteKey Remote { get; init; }
    public string? Value { get; init; }
    public SourceReviewMeaning? Status { get; init; }
    public string? Comment { get; init; }
    public string? NewName { get; init; }
    public string? ExpectedRevision { get; init; }

    /// <summary>What the baseline held, for reconciliation after an unclear failure.</summary>
    public string? BaseValue { get; init; }

    /// <summary>For a created key: remote language → value.</summary>
    public IReadOnlyDictionary<string, string>? Values { get; init; }
    public bool Include { get; init; } = true;
}

public enum SyncConflictKind
{
    BothChanged,

    /// <summary>Changed here, deleted there.</summary>
    EditedLocallyDeletedRemotely,

    /// <summary>Deleted here, changed there.</summary>
    DeletedLocallyEditedRemotely,

    /// <summary>No baseline: the two sides differ and there is no common ancestor.</summary>
    FirstSync,
}

public sealed record SyncConflict
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SyncConflictKind Kind { get; init; }
    public required SyncField Field { get; init; }
    public Guid? EntryId { get; init; }
    public required string Language { get; init; }
    public required SyncRemoteKey Remote { get; init; }
    public string? Base { get; init; }
    public string? Local { get; init; }
    public string? RemoteValue { get; init; }
    public string? RemoteRevision { get; init; }
    public SourceReviewMeaning? RemoteStatus { get; init; }
    public TranslationPath Namespace { get; init; } = TranslationPath.Empty;
    public TranslationPath KeyPath { get; init; } = TranslationPath.Empty;
}

public enum SyncNoticeKind
{
    /// <summary>The service cannot do this (rename, status), so the change is held.</summary>
    Unsupported,

    /// <summary>A local deletion that is not pushed.</summary>
    DeletionNotPushed,

    /// <summary>The read was partial, so no remote deletion can be inferred.</summary>
    PartialRead,

    /// <summary>Pull first: the service cannot check revisions, so a push could overwrite.</summary>
    PullFirst,
    UnmappedLanguage,

    /// <summary>Strict validation is on and the value has validation errors, so it is not sent.</summary>
    HeldBackInvalid,
    Info,
}

public sealed record SyncNotice(SyncNoticeKind Kind, string Message, Guid? EntryId = null, string? Language = null);

public static class SyncDeliveryPolicy
{
    /// <summary>
    /// Takes the pushes of <paramref name="blocked"/> translations out of <paramref name="plan"/> and explains each in a
    /// notice. Deletions still go out: removing a value never delivers an invalid one.
    /// </summary>
    public static SyncPlan HoldBackInvalid(SyncPlan plan, IReadOnlySet<(Guid EntryId, string Language)> blocked)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(blocked);
        if (blocked.Count == 0) return plan;
        bool Held(PushChange c) => c.Kind is not (PushKind.DeleteKey or PushKind.DeleteTranslation) && blocked.Contains((c.EntryId, c.Language));
        var held = plan.Push.Where(Held).ToList();
        if (held.Count == 0) return plan;
        var notices = held.GroupBy(c => (c.EntryId, c.Language)).Select(g => new SyncNotice(SyncNoticeKind.HeldBackInvalid,
            $"{g.First().Remote.Name} [{g.Key.Language}] has validation errors and is not sent while strict validation is on.", g.Key.EntryId, g.Key.Language));
        return plan with { Push = [.. plan.Push.Where(c => !Held(c))], Notices = [.. plan.Notices, .. notices] };
    }

    /// <summary>
    /// For a plan that sends whole files: a file cannot go out without its invalid values, so every upload of a file in
    /// <paramref name="blocked"/> is taken out with a notice naming the values. When some blocked values cannot be placed in a
    /// file, every upload is held. Deletions still go out.
    /// </summary>
    public static SyncPlan HoldBackInvalidDocuments(SyncPlan plan, Services.PolicyBlockedFiles blocked)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(blocked);
        if (blocked.IsEmpty) return plan;
        static bool Upload(PushChange c) => c.Kind is not (PushKind.DeleteKey or PushKind.DeleteTranslation);
        bool Held(PushChange c) => Upload(c) && (blocked.Unplaced.Count > 0 || blocked.ByPath.ContainsKey(c.Remote.Name));
        var held = plan.Push.Where(Held).ToList();
        if (held.Count == 0) return plan;
        var notices = held.Select(c =>
        {
            var values = blocked.ByPath.GetValueOrDefault(c.Remote.Name) ?? blocked.Unplaced;
            return new SyncNotice(SyncNoticeKind.HeldBackInvalid,
                $"{c.Remote.Name} is not sent while strict validation is on: {Describe(values)} {(values.Count == 1 ? "has" : "have")} validation errors. Fix and save, then push again.");
        });
        return plan with { Push = [.. plan.Push.Where(c => !Held(c))], Notices = [.. plan.Notices, .. notices] };
    }

    private static string Describe(IReadOnlyList<string> values) =>
        values.Count <= 3 ? string.Join(", ", values) : $"{string.Join(", ", values.Take(3))} and {values.Count - 3} more";
}

/// <summary>An immutable proposal, made from a captured workspace snapshot and a completed remote read.</summary>
public sealed record SyncPlan(string ConnectionId, Guid PlanId, SyncPlanKind Kind, long WorkspaceRevision, DateTimeOffset RemoteReadAt,
    bool RemoteReadComplete, bool IsFirstSync, IReadOnlyList<PullChange> Pull, IReadOnlyList<PushChange> Push,
    IReadOnlyList<SyncConflict> Conflicts, IReadOnlyList<SyncNotice> Notices)
{
    /// <summary>Translations no longer on either side; their baseline entries are dropped when the plan applies.</summary>
    public IReadOnlyList<(Guid EntryId, string Language)> Forget { get; init; } = [];

    /// <summary>True for a connection that stores whole files: changes name a document path (<see cref="SyncRemoteKey.Name"/>) and no translation.</summary>
    public bool IsDocumentPlan { get; init; }

    public bool HasChanges => Pull.Any(c => c.Include && c.Kind != PullKind.Converge) || Push.Any(c => c.Include) || Conflicts.Count > 0;
    /// <summary>Nothing for the user to decide, but the baseline still has to move: both sides agree, or a translation is gone from both.</summary>
    public bool HasBookkeeping => Forget.Count > 0 || Pull.Any(c => c.Kind == PullKind.Converge);
    public bool HasConflicts => Conflicts.Count > 0;
    public int PullCount => Pull.Count(c => c.Include && c.Kind != PullKind.Converge);
    public int PushCount => Push.Count(c => c.Include);

    public SyncPlan Exclude(params Guid[] changeIds)
    {
        var ids = changeIds.ToHashSet();
        return this with
        {
            Pull = [.. Pull.Select(c => ids.Contains(c.Id) ? c with { Include = false } : c)],
            Push = [.. Push.Select(c => ids.Contains(c.Id) ? c with { Include = false } : c)],
        };
    }

    public SyncPlan Include(params Guid[] changeIds)
    {
        var ids = changeIds.ToHashSet();
        return this with
        {
            Pull = [.. Pull.Select(c => ids.Contains(c.Id) ? c with { Include = true } : c)],
            Push = [.. Push.Select(c => ids.Contains(c.Id) ? c with { Include = true } : c)],
        };
    }
}

/// <summary>How the user settled one conflict.</summary>
public enum SyncChoice
{
    TakeLocal,
    TakeRemote,
    Merged,

    /// <summary>Leave it listed; neither side changes.</summary>
    Defer,
}

public sealed record SyncResolution(Guid ConflictId, SyncChoice Choice, string? MergedValue = null);

/// <summary>Turns conflict choices into changes. Deferred conflicts stay in the plan and block nothing else.</summary>
public static class SyncPlanResolver
{
    public static SyncPlan Resolve(SyncPlan plan, IEnumerable<SyncResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolutions);
        var byId = resolutions.ToDictionary(r => r.ConflictId);
        var remaining = new List<SyncConflict>();
        var pull = plan.Pull.ToList();
        var push = plan.Push.ToList();
        foreach (var conflict in plan.Conflicts)
        {
            if (!byId.TryGetValue(conflict.Id, out var resolution) || resolution.Choice == SyncChoice.Defer) { remaining.Add(conflict); continue; }
            switch (resolution.Choice)
            {
                case SyncChoice.TakeRemote:
                    pull.Add(TakeRemote(conflict));
                    break;
                case SyncChoice.TakeLocal:
                    // Revisions are advanced to the remote's current one, so the conditional write succeeds against what the user saw.
                    if (TakeLocal(conflict, conflict.Local) is { } local) push.Add(local);
                    break;
                case SyncChoice.Merged:
                    if (conflict.Remote.ResourceId == DocumentSyncService.Resource)
                        throw new ArgumentException("A document conflict cannot be merged as text; take the local or the remote file.", nameof(resolutions));
                    if (resolution.MergedValue is null) throw new ArgumentException("A merged resolution needs the merged value.", nameof(resolutions));
                    // The merged text goes to both sides: the service gets it and the project adopts it.
                    pull.Add(TakeRemote(conflict) with { NewValue = resolution.MergedValue, Field = SyncField.Value, Kind = conflict.EntryId is null ? PullKind.Create : PullKind.Update });
                    if (TakeLocal(conflict, resolution.MergedValue) is { } merged) push.Add(merged);
                    break;
            }
        }
        return plan with { Pull = pull, Push = push, Conflicts = remaining };
    }

    private static PullChange TakeRemote(SyncConflict c) => c.Remote.ResourceId == DocumentSyncService.Resource
        ? new()
        {
            Kind = c.Kind switch
            {
                SyncConflictKind.EditedLocallyDeletedRemotely => PullKind.Delete,
                SyncConflictKind.DeletedLocallyEditedRemotely => PullKind.Create,
                _ => PullKind.Update,
            },
            Field = SyncField.Existence, Language = string.Empty, Remote = c.Remote, RemoteRevision = c.RemoteRevision,
        }
        : new()
    {
        Kind = c.Kind == SyncConflictKind.EditedLocallyDeletedRemotely ? PullKind.Delete : c.EntryId is null ? PullKind.Create : PullKind.Update,
        Field = c.Field,
        EntryId = c.EntryId,
        Language = c.Language,
        Remote = c.Remote,
        Namespace = c.Namespace,
        KeyPath = c.KeyPath,
        LocalValue = c.Local,
        NewValue = c.Field == SyncField.Value ? c.RemoteValue : null,
        NewStatus = c.Field == SyncField.Status ? c.RemoteStatus : null,
        NewComment = c.Field == SyncField.Comment ? c.RemoteValue : null,
        RemoteRevision = c.RemoteRevision,
    };

    private static PushChange? TakeLocal(SyncConflict c, string? value)
    {
        if (c.Remote.ResourceId == DocumentSyncService.Resource)
            return new()
            {
                Kind = c.Kind switch
                {
                    SyncConflictKind.EditedLocallyDeletedRemotely => PushKind.Create,
                    SyncConflictKind.DeletedLocallyEditedRemotely => PushKind.DeleteKey,
                    _ => PushKind.Value,
                },
                Language = string.Empty, Remote = c.Remote,
                ExpectedRevision = c.Kind == SyncConflictKind.EditedLocallyDeletedRemotely ? null : c.RemoteRevision,
            };
        if (c.EntryId is not { } entry) return null;
        if (c.Kind == SyncConflictKind.DeletedLocallyEditedRemotely)
            return new() { Kind = PushKind.DeleteTranslation, EntryId = entry, Language = c.Language, Remote = c.Remote, ExpectedRevision = c.RemoteRevision, BaseValue = c.Base };
        return new()
        {
            Kind = c.Field switch { SyncField.Status => PushKind.Status, SyncField.Comment => PushKind.Comment, SyncField.Name => PushKind.Rename, _ => PushKind.Value },
            EntryId = entry,
            Language = c.Language,
            Remote = c.Remote,
            Value = c.Field == SyncField.Value ? value : null,
            Comment = c.Field == SyncField.Comment ? value : null,
            NewName = c.Field == SyncField.Name ? value : null,
            ExpectedRevision = c.RemoteRevision,
            BaseValue = c.Base,
        };
    }
}
