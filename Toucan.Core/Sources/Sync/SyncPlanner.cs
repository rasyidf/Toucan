using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>A translation of the open project as the planner sees it.</summary>
public sealed record SyncLocalUnit(Guid EntryId, string Language, TranslationPath Namespace, TranslationPath KeyPath, string Value,
    string Comment, SourceReviewMeaning Status);

/// <param name="Connection">The connection's mapping and policy.</param>
/// <param name="Baseline">What both sides last agreed on; null means a first sync.</param>
/// <param name="Capabilities">What the adapter declares, narrowed by the connection's permissions.</param>
/// <param name="RemoteComplete">Whether the remote read finished. A partial read never leads to an inferred deletion.</param>
public sealed record SyncPlanRequest(string ConnectionId, SourceConnectionSettings Connection, SyncPlanKind Kind, SyncBaseline? Baseline,
    IReadOnlyList<SyncLocalUnit> Local, IReadOnlyList<RemoteUnit> Remote, bool RemoteComplete, SourceCapabilities Capabilities,
    long WorkspaceRevision, DateTimeOffset RemoteReadAt)
{
    /// <summary>The remote resource a project namespace belongs to. The default uses the connection's resource map.</summary>
    public Func<TranslationPath, string>? ResolveResource { get; init; }
}

/// <summary>
/// The three-way comparison of design 7.2. Pure: no I/O, no clock, no randomness beyond fresh change IDs. Fields compare
/// independently, so a remote status change and a local value change on one unit are not a conflict.
/// </summary>
public static class SyncPlanner
{
    public const string DefaultResource = "default";

    private enum Decision { None, Push, Pull, Converge, Conflict }

    public static SyncPlan Plan(SyncPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var connection = request.Connection;
        var mapper = new SourceKeyMapper(connection.Keys);
        var caps = request.Capabilities;
        var firstSync = request.Baseline is null;
        var baseline = request.Baseline ?? new SyncBaseline();
        var toRemoteLanguage = connection.Languages;
        var toLocalLanguage = connection.Languages.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

        var pull = new List<PullChange>();
        var push = new List<PushChange>();
        var conflicts = new List<SyncConflict>();
        var notices = new List<SyncNotice>();
        var forget = new List<(Guid, string)>();

        string Resource(TranslationPath ns) => request.ResolveResource?.Invoke(ns) ?? DefaultResourceFor(connection, ns);

        // Remote lookup by service ID, then by name.
        var remoteByName = new Dictionary<(string, string, string), RemoteUnit>();
        var remoteById = new Dictionary<(string, string), RemoteUnit>();
        foreach (var unit in request.Remote)
        {
            remoteByName[(unit.Key.ResourceId, Normalize(unit.Key.Name, mapper), unit.Language)] = unit;
            if (unit.Key.RemoteId is { } id) remoteById[(id, unit.Language)] = unit;
        }
        var matchedRemote = new HashSet<RemoteUnit>(ReferenceEqualityComparer.Instance);
        var baselineByUnit = baseline.Units.GroupBy(b => (b.EntryId, b.Language)).ToDictionary(g => g.Key, g => g.First());
        var localKeys = new HashSet<(Guid, string)>();

        RemoteUnit? FindRemote(BaselineUnit? b, string resource, string name, string remoteLanguage)
        {
            if (b?.Remote.RemoteId is { } id && remoteById.TryGetValue((id, remoteLanguage), out var byId)) return byId;
            if (b is not null && remoteByName.TryGetValue((b.Remote.ResourceId, Normalize(b.Remote.Name, mapper), remoteLanguage), out var old)) return old;
            return remoteByName.GetValueOrDefault((resource, Normalize(name, mapper), remoteLanguage));
        }

        foreach (var local in request.Local)
        {
            if (!toRemoteLanguage.TryGetValue(local.Language, out var remoteLanguage))
            {
                notices.Add(new(SyncNoticeKind.UnmappedLanguage, $"{local.Language} is not mapped to a remote language, so it is not synced.", local.EntryId, local.Language));
                continue;
            }
            localKeys.Add((local.EntryId, local.Language));
            baselineByUnit.TryGetValue((local.EntryId, local.Language), out var b);
            var resource = Resource(local.Namespace);
            var name = mapper.ToRemote(local.Namespace, local.KeyPath);
            var remote = FindRemote(b, resource, name, remoteLanguage);
            if (remote is not null) matchedRemote.Add(remote);
            var key = remote is null ? new SyncRemoteKey(resource, name) : new SyncRemoteKey(remote.Key.ResourceId, remote.Key.Name, remote.Key.RemoteId ?? b?.Remote.RemoteId);

            if (remote is null)
            {
                if (b is null || firstSync)
                {
                    // New here: create it at the service.
                    AddCreate(local, key, remoteLanguage, push, notices, caps);
                }
                else if (!request.RemoteComplete)
                {
                    notices.Add(new(SyncNoticeKind.PartialRead, $"The read was incomplete, so {name} is not treated as deleted at the service.", local.EntryId, local.Language));
                }
                else if (local.Value == b.Value)
                {
                    // Unchanged here, gone there: pull the deletion, but only when the user includes it.
                    pull.Add(new() { Kind = PullKind.Delete, Field = SyncField.Existence, EntryId = local.EntryId, Language = local.Language, Remote = b.Remote,
                        Namespace = local.Namespace, KeyPath = local.KeyPath, LocalValue = local.Value, Include = false });
                }
                else
                {
                    conflicts.Add(new() { Kind = SyncConflictKind.EditedLocallyDeletedRemotely, Field = SyncField.Existence, EntryId = local.EntryId, Language = local.Language,
                        Remote = b.Remote, Base = b.Value, Local = local.Value, RemoteValue = null, Namespace = local.Namespace, KeyPath = local.KeyPath });
                }
                continue;
            }

            if (b is null)
            {
                // No common ancestor for this unit.
                if (remote.Value == local.Value)
                    pull.Add(Converge(local, key, remote));
                else
                    conflicts.Add(new() { Kind = SyncConflictKind.FirstSync, Field = SyncField.Value, EntryId = local.EntryId, Language = local.Language, Remote = key,
                        Local = local.Value, RemoteValue = remote.Value, RemoteRevision = remote.Revision, RemoteStatus = remote.Status?.Meaning, Namespace = local.Namespace, KeyPath = local.KeyPath });
                continue;
            }

            // Renames.
            var renamedHere = !string.Equals(Normalize(b.Remote.Name, mapper), Normalize(name, mapper), StringComparison.Ordinal);
            var renamedThere = !string.Equals(Normalize(b.Remote.Name, mapper), Normalize(remote.Key.Name, mapper), StringComparison.Ordinal);
            if (renamedHere && !renamedThere)
            {
                if (caps.HasFlag(SourceCapabilities.RenameKeys))
                    push.Add(new() { Kind = PushKind.Rename, EntryId = local.EntryId, Language = local.Language, Remote = key, NewName = name, ExpectedRevision = remote.Revision, BaseValue = b.Value });
                else
                    notices.Add(new(SyncNoticeKind.Unsupported, $"Renaming {b.Remote.Name} to {name} is not supported by this service, so it is held.", local.EntryId, local.Language));
            }
            else if (renamedThere && !renamedHere)
            {
                pull.Add(new() { Kind = PullKind.Rename, Field = SyncField.Name, EntryId = local.EntryId, Language = local.Language, Remote = key,
                    Namespace = local.Namespace, KeyPath = new(mapper.FromRemote(remote.Key.Name)), LocalValue = local.Value, RemoteRevision = remote.Revision });
            }
            else if (renamedHere && renamedThere && !string.Equals(Normalize(name, mapper), Normalize(remote.Key.Name, mapper), StringComparison.Ordinal))
            {
                conflicts.Add(new() { Kind = SyncConflictKind.BothChanged, Field = SyncField.Name, EntryId = local.EntryId, Language = local.Language, Remote = key,
                    Base = b.Remote.Name, Local = name, RemoteValue = remote.Key.Name, RemoteRevision = remote.Revision, Namespace = local.Namespace, KeyPath = local.KeyPath });
            }

            // Value.
            switch (Decide(b.Value, local.Value, remote.Value))
            {
                case Decision.Push:
                    if (caps.HasFlag(SourceCapabilities.WriteValues))
                        push.Add(new() { Kind = PushKind.Value, EntryId = local.EntryId, Language = local.Language, Remote = key, Value = local.Value, ExpectedRevision = ExpectedRevision(caps, remote), BaseValue = b.Value });
                    else
                        notices.Add(new(SyncNoticeKind.Unsupported, $"This service does not accept value changes, so {name} ({local.Language}) is held.", local.EntryId, local.Language));
                    break;
                case Decision.Pull:
                    pull.Add(new() { Kind = PullKind.Update, Field = SyncField.Value, EntryId = local.EntryId, Language = local.Language, Remote = key, Namespace = local.Namespace,
                        KeyPath = local.KeyPath, LocalValue = local.Value, NewValue = remote.Value, RemoteRevision = remote.Revision });
                    break;
                case Decision.Converge:
                    pull.Add(Converge(local, key, remote));
                    break;
                case Decision.Conflict:
                    conflicts.Add(new() { Kind = SyncConflictKind.BothChanged, Field = SyncField.Value, EntryId = local.EntryId, Language = local.Language, Remote = key,
                        Base = b.Value, Local = local.Value, RemoteValue = remote.Value, RemoteRevision = remote.Revision, Namespace = local.Namespace, KeyPath = local.KeyPath });
                    break;
            }

            // Status, only when the service has review states and the connection syncs them.
            if (connection.Policy.PushStatuses && caps.HasFlag(SourceCapabilities.ReviewStatus) && remote.Status is { } remoteStatus)
            {
                switch (Decide(b.Status?.ToString(), local.Status.ToString(), remoteStatus.Meaning.ToString()))
                {
                    case Decision.Push:
                        push.Add(new() { Kind = PushKind.Status, EntryId = local.EntryId, Language = local.Language, Remote = key, Status = local.Status, ExpectedRevision = ExpectedRevision(caps, remote) });
                        break;
                    case Decision.Pull:
                        pull.Add(new() { Kind = PullKind.Update, Field = SyncField.Status, EntryId = local.EntryId, Language = local.Language, Remote = key, Namespace = local.Namespace,
                            KeyPath = local.KeyPath, LocalValue = local.Value, NewStatus = remoteStatus.Meaning, RemoteRevision = remote.Revision });
                        break;
                    case Decision.Conflict:
                        conflicts.Add(new() { Kind = SyncConflictKind.BothChanged, Field = SyncField.Status, EntryId = local.EntryId, Language = local.Language, Remote = key,
                            Base = b.Status?.ToString(), Local = local.Status.ToString(), RemoteValue = remoteStatus.Meaning.ToString(), RemoteStatus = remoteStatus.Meaning,
                            RemoteRevision = remote.Revision, Namespace = local.Namespace, KeyPath = local.KeyPath });
                        break;
                }
            }

            // Comment.
            if (connection.Policy.PushComments && caps.HasFlag(SourceCapabilities.Comments))
            {
                var remoteComment = remote.Comment ?? string.Empty;
                switch (Decide(b.CommentHash, BaselineUnit.Hash(local.Comment), BaselineUnit.Hash(remoteComment)))
                {
                    case Decision.Push:
                        push.Add(new() { Kind = PushKind.Comment, EntryId = local.EntryId, Language = local.Language, Remote = key, Comment = local.Comment });
                        break;
                    case Decision.Pull:
                        pull.Add(new() { Kind = PullKind.Update, Field = SyncField.Comment, EntryId = local.EntryId, Language = local.Language, Remote = key, Namespace = local.Namespace,
                            KeyPath = local.KeyPath, LocalValue = local.Value, NewComment = remoteComment, RemoteRevision = remote.Revision });
                        break;
                    case Decision.Conflict:
                        conflicts.Add(new() { Kind = SyncConflictKind.BothChanged, Field = SyncField.Comment, EntryId = local.EntryId, Language = local.Language, Remote = key,
                            Local = local.Comment, RemoteValue = remoteComment, RemoteRevision = remote.Revision, Namespace = local.Namespace, KeyPath = local.KeyPath });
                        break;
                }
            }
        }

        // Bound translations that are no longer in the project.
        foreach (var b in baseline.Units.Where(u => !localKeys.Contains((u.EntryId, u.Language)) && toRemoteLanguage.ContainsKey(u.Language)))
        {
            var remoteLanguage = toRemoteLanguage[b.Language];
            var remote = FindRemote(b, b.Remote.ResourceId, b.Remote.Name, remoteLanguage);
            if (remote is not null) matchedRemote.Add(remote);
            if (remote is null)
            {
                forget.Add((b.EntryId, b.Language)); // gone from both sides
                continue;
            }
            if (remote.Value != b.Value)
            {
                conflicts.Add(new() { Kind = SyncConflictKind.DeletedLocallyEditedRemotely, Field = SyncField.Existence, EntryId = b.EntryId, Language = b.Language,
                    Remote = new(remote.Key.ResourceId, remote.Key.Name, remote.Key.RemoteId), Base = b.Value, Local = null, RemoteValue = remote.Value, RemoteRevision = remote.Revision });
                continue;
            }
            // Unchanged at the service: a local deletion.
            var kind = caps.HasFlag(SourceCapabilities.DeleteKeys) ? PushKind.DeleteKey
                : caps.HasFlag(SourceCapabilities.DeleteTranslations) ? PushKind.DeleteTranslation : (PushKind?)null;
            var policy = connection.Policy.DeleteRemote;
            if (policy == "never" || kind is null)
            {
                notices.Add(new(SyncNoticeKind.DeletionNotPushed, $"{b.Remote.Name} ({b.Language}) was deleted here and stays at the service.", b.EntryId, b.Language));
                continue;
            }
            push.Add(new() { Kind = kind.Value, EntryId = b.EntryId, Language = b.Language, Remote = new(remote.Key.ResourceId, remote.Key.Name, remote.Key.RemoteId),
                ExpectedRevision = ExpectedRevision(caps, remote), BaseValue = b.Value, Include = policy == "always" });
            if (policy == "ask")
                notices.Add(new(SyncNoticeKind.DeletionNotPushed, $"{b.Remote.Name} ({b.Language}) was deleted here; include it to delete it at the service.", b.EntryId, b.Language));
        }

        // Keys that exist only at the service.
        var created = new Dictionary<(string, string), Dictionary<string, string>>();
        var createdSample = new Dictionary<(string, string), RemoteUnit>();
        foreach (var remote in request.Remote.Where(r => !matchedRemote.Contains(r)))
        {
            if (!toLocalLanguage.TryGetValue(remote.Language, out var localLanguage)) continue;
            var cell = (remote.Key.ResourceId, Normalize(remote.Key.Name, mapper));
            if (!created.TryGetValue(cell, out var values)) created[cell] = values = new(StringComparer.Ordinal);
            values[localLanguage] = remote.Value;
            createdSample.TryAdd(cell, remote);
        }
        foreach (var (cell, values) in created)
        {
            var sample = createdSample[cell];
            var path = new TranslationPath(mapper.FromRemote(sample.Key.Name));
            var remoteKey = new SyncRemoteKey(sample.Key.ResourceId, sample.Key.Name, sample.Key.RemoteId);
            foreach (var (language, value) in values)
            {
                var unit = request.Remote.First(r => r.Key.ResourceId == cell.Item1 && Normalize(r.Key.Name, mapper) == cell.Item2 && toLocalLanguage.GetValueOrDefault(r.Language) == language);
                pull.Add(new() { Kind = PullKind.Create, Field = SyncField.Existence, Language = language, Remote = remoteKey, KeyPath = path, NewValue = value,
                    NewStatus = unit.Status?.Meaning, NewComment = unit.Comment, RemoteRevision = unit.Revision, NewValues = values });
            }
        }

        // Partial reads, and services that cannot check revisions.
        if (!request.RemoteComplete)
            notices.Add(new(SyncNoticeKind.PartialRead, "The service did not finish listing its content. Nothing was treated as deleted there."));
        if (firstSync && conflicts.Count > 0)
            notices.Add(new(SyncNoticeKind.Info, $"First sync: {conflicts.Count} translations differ and there is no common ancestor. Choose a side for all or review each."));
        if (request.Kind == SyncPlanKind.Push && !caps.HasFlag(SourceCapabilities.ConditionalWrites) && (pull.Any(c => c.Kind != PullKind.Converge) || conflicts.Count > 0))
        {
            var pinned = pull.Where(c => c.Kind != PullKind.Converge && c.EntryId is not null).Select(c => (c.EntryId!.Value, c.Language))
                .Concat(conflicts.Where(c => c.EntryId is not null).Select(c => (c.EntryId!.Value, c.Language))).ToHashSet();
            var held = push.RemoveAll(p => pinned.Contains((p.EntryId, p.Language)));
            if (held > 0) notices.Add(new(SyncNoticeKind.PullFirst, $"{held} changes were held: the service cannot check revisions and it changed. Pull first."));
        }

        return request.Kind switch
        {
            SyncPlanKind.Pull => Build(request, firstSync, pull, [], conflicts, notices, forget),
            SyncPlanKind.Push => Build(request, firstSync, pull.Where(c => c.Kind == PullKind.Converge).ToList(), push, conflicts, notices, forget),
            _ => Build(request, firstSync, pull, push, conflicts, notices, forget),
        };
    }

    /// <summary>Bulk choice for a first sync: settle every first-sync conflict the same way.</summary>
    public static SyncPlan ResolveAll(SyncPlan plan, SyncChoice choice) =>
        SyncPlanResolver.Resolve(plan, plan.Conflicts.Where(c => c.Kind == SyncConflictKind.FirstSync).Select(c => new SyncResolution(c.Id, choice)));

    private static SyncPlan Build(SyncPlanRequest r, bool first, List<PullChange> pull, List<PushChange> push, List<SyncConflict> conflicts, List<SyncNotice> notices, List<(Guid, string)> forget) =>
        new(r.ConnectionId, Guid.NewGuid(), r.Kind, r.WorkspaceRevision, r.RemoteReadAt, r.RemoteComplete, first, pull, push, conflicts, notices) { Forget = forget };

    private static void AddCreate(SyncLocalUnit local, SyncRemoteKey key, string remoteLanguage, List<PushChange> push, List<SyncNotice> notices, SourceCapabilities caps)
    {
        if (!caps.HasFlag(SourceCapabilities.CreateKeys))
        {
            notices.Add(new(SyncNoticeKind.Unsupported, $"{key.Name} is new here, but this service cannot create keys, so it is held.", local.EntryId, local.Language));
            return;
        }
        push.Add(new() { Kind = PushKind.Create, EntryId = local.EntryId, Language = local.Language, Remote = key, Value = local.Value,
            Values = new Dictionary<string, string> { [remoteLanguage] = local.Value } });
    }

    private static PullChange Converge(SyncLocalUnit local, SyncRemoteKey key, RemoteUnit remote) =>
        new() { Kind = PullKind.Converge, Field = SyncField.Value, EntryId = local.EntryId, Language = local.Language, Remote = key, Namespace = local.Namespace,
            KeyPath = local.KeyPath, LocalValue = local.Value, NewValue = remote.Value, NewStatus = remote.Status?.Meaning, RemoteRevision = remote.Revision };

    private static Decision Decide(string? baseValue, string? local, string? remote)
    {
        var localChanged = !string.Equals(local, baseValue, StringComparison.Ordinal);
        var remoteChanged = !string.Equals(remote, baseValue, StringComparison.Ordinal);
        if (!localChanged && !remoteChanged) return Decision.None;
        if (localChanged && !remoteChanged) return Decision.Push;
        if (!localChanged) return Decision.Pull;
        return string.Equals(local, remote, StringComparison.Ordinal) ? Decision.Converge : Decision.Conflict;
    }

    private static string? ExpectedRevision(SourceCapabilities caps, RemoteUnit remote) =>
        caps.HasFlag(SourceCapabilities.ConditionalWrites) ? remote.Revision : null;

    private static string Normalize(string name, SourceKeyMapper mapper) => mapper.Comparer == StringComparer.OrdinalIgnoreCase ? name.ToLowerInvariant() : name;

    private static string DefaultResourceFor(SourceConnectionSettings connection, TranslationPath ns)
    {
        if (connection.Resources.Count == 0) return DefaultResource;
        var package = ns.Segments.Count > 0 ? ns.Segments[0] : null;
        return (connection.Resources.FirstOrDefault(r => r.Package is not null && r.Package == package) ?? connection.Resources.FirstOrDefault(r => r.Package is null) ?? connection.Resources[0]).Remote;
    }
}
