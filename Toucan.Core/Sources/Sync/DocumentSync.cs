using System.Security.Cryptography;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>
/// Sync for a connection that stores whole files (a document session). The comparison is per document, with the same
/// three-way rules as for translations: the baseline holds the service's version and the local file's hash from when both sides
/// last agreed. Plans use the ordinary <see cref="SyncPlan"/>, with the document path as the change's key, so the plan dialog,
/// the CLI and conflict choices work unchanged. Local files and the service's documents are matched by relative path.
/// </summary>
public sealed class DocumentSyncService(TimeProvider? time = null)
{
    public const string Resource = "document";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private static SyncRemoteKey Key(string path) => new(Resource, path);

    public async Task<SyncPlan> PlanAsync(string connectionId, SourceConnectionSettings connection, SyncPlanKind kind, IDocumentSourceSession session,
        SourceCapabilities capabilities, SyncStateStore store, string root, IEnumerable<string> localCandidates, long workspaceRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        var remote = new Dictionary<string, RemoteDocumentInfo>(StringComparer.Ordinal);
        var complete = false;
        await foreach (var page in session.ListDocumentsAsync(new(), cancellationToken).ConfigureAwait(false))
        {
            foreach (var d in page.Documents) remote[Normalize(d.Ref.Path)] = d;
            complete = page.IsComplete;
        }

        var baselineFile = store.LoadBaseline();
        var baseline = (baselineFile?.Documents ?? []).ToDictionary(d => d.Path, StringComparer.Ordinal);
        var firstSync = baselineFile is null;
        var paths = remote.Keys.Concat(localCandidates.Select(Normalize)).Where(p => !p.StartsWith(".toucan/", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var pull = new List<PullChange>();
        var push = new List<PushChange>();
        var conflicts = new List<SyncConflict>();
        var notices = new List<SyncNotice>();
        var forget = new List<(Guid, string)>();
        var canWrite = capabilities.HasFlag(SourceCapabilities.WriteDocuments);
        var policy = connection.Policy.DeleteRemote;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var local = LocalHash(root, path);
            var has = remote.TryGetValue(path, out var info);
            baseline.TryGetValue(path, out var b);
            var key = Key(path);

            SyncConflict Conflict(SyncConflictKind kind, string? localText, string? remoteText) => new()
            {
                Kind = kind, Field = SyncField.Value, Language = string.Empty, Remote = key, Base = b is null ? null : Short(b.LocalHash), Local = localText, RemoteValue = remoteText,
                RemoteRevision = info?.Version,
            };

            if (has && local is not null)
            {
                var localChanged = b is null || local != b.LocalHash;
                var remoteChanged = b is null || info!.Version != b.Version;
                if (!localChanged && !remoteChanged) continue;
                if (localChanged && !remoteChanged)
                {
                    AddPush(path, info!.Version, local, push, notices, canWrite);
                    continue;
                }
                if (!localChanged)
                {
                    pull.Add(new() { Kind = PullKind.Update, Field = SyncField.Value, Language = string.Empty, Remote = key, LocalValue = local, RemoteRevision = info!.Version });
                    continue;
                }
                // Changed on both sides, or never compared: the same content is no conflict.
                var remoteBytes = (await session.ReadDocumentAsync(new(path), cancellationToken).ConfigureAwait(false)).Bytes;
                if (Hash(remoteBytes.Span) == local)
                    pull.Add(new() { Kind = PullKind.Converge, Field = SyncField.Value, Language = string.Empty, Remote = key, LocalValue = local, RemoteRevision = info!.Version });
                else
                    conflicts.Add(Conflict(b is null ? SyncConflictKind.FirstSync : SyncConflictKind.BothChanged, "local file " + Short(local), "remote version " + Short(info!.Version)));
            }
            else if (has)
            {
                if (b is null) pull.Add(new() { Kind = PullKind.Create, Field = SyncField.Existence, Language = string.Empty, Remote = key, RemoteRevision = info!.Version });
                else if (info!.Version == b.Version)
                {
                    if (policy == "never" || !canWrite) notices.Add(new(SyncNoticeKind.DeletionNotPushed, $"{path} was deleted here and stays at the service."));
                    else push.Add(new() { Kind = PushKind.DeleteKey, Language = string.Empty, Remote = key, ExpectedRevision = info.Version, Include = policy == "always" });
                }
                else conflicts.Add(Conflict(SyncConflictKind.DeletedLocallyEditedRemotely, null, "remote version " + Short(info.Version)));
            }
            else if (local is not null)
            {
                if (b is null) AddPush(path, null, local, push, notices, canWrite);
                else if (!complete) notices.Add(new(SyncNoticeKind.PartialRead, $"The read was incomplete, so {path} is not treated as deleted at the service."));
                else if (local == b.LocalHash) pull.Add(new() { Kind = PullKind.Delete, Field = SyncField.Existence, Language = string.Empty, Remote = key, LocalValue = local, Include = false });
                else conflicts.Add(Conflict(SyncConflictKind.EditedLocallyDeletedRemotely, "local file " + Short(local), null));
            }
            else if (b is not null) forget.Add((Guid.Empty, path));
        }
        if (!complete) notices.Add(new(SyncNoticeKind.PartialRead, "The service did not finish listing its documents. Nothing was treated as deleted there."));
        if (firstSync && conflicts.Count > 0)
            notices.Add(new(SyncNoticeKind.Info, $"First sync: {conflicts.Count} documents differ and there is no common version yet. Choose a side for all or review each."));

        var plan = new SyncPlan(connectionId, Guid.NewGuid(), kind, workspaceRevision, _time.GetUtcNow(), complete, firstSync,
            kind == SyncPlanKind.Push ? [.. pull.Where(c => c.Kind == PullKind.Converge)] : pull, kind == SyncPlanKind.Pull ? [] : push, conflicts, notices)
        { Forget = forget, IsDocumentPlan = true };
        return plan;
    }

    private static void AddPush(string path, string? remoteVersion, string localHash, List<PushChange> push, List<SyncNotice> notices, bool canWrite)
    {
        if (!canWrite)
        {
            notices.Add(new(SyncNoticeKind.Unsupported, $"This service cannot be written to, so {path} is held."));
            return;
        }
        push.Add(new() { Kind = remoteVersion is null ? PushKind.Create : PushKind.Value, Language = string.Empty, Remote = Key(path), ExpectedRevision = remoteVersion, BaseValue = localHash });
    }

    /// <summary>Applies the pull and push sides of a document plan. Each document stands alone: nothing is rolled back.</summary>
    public async Task<(PullResult Pull, PushResult Push)> ApplyAsync(SyncPlan plan, IDocumentSourceSession session, SyncStateStore store, string root,
        IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var file = store.LoadBaseline() ?? new SyncBaseline();
        var docs = file.Documents.ToDictionary(d => d.Path, StringComparer.Ordinal);
        var now = _time.GetUtcNow();
        var pulled = 0;
        var created = 0;
        var deleted = 0;
        var skipped = new List<PullChange>();
        string? refusal = null;
        var confirmed = 0;
        var failed = 0;
        var unknown = 0;
        var errors = new List<string>();
        var conflicts = new List<SyncConflict>();
        var work = plan.Pull.Count(c => c.Include) + plan.Push.Count(c => c.Include);
        var done = 0;

        foreach (var change in plan.Pull.Where(c => c.Include))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = change.Remote.Name;
            var full = Full(root, path);
            progress?.Report(new(++done, work, $"Pulling {path}"));
            if (change.Kind == PullKind.Converge)
            {
                docs[path] = new BaselineDocument { Path = path, Version = change.RemoteRevision ?? string.Empty, LocalHash = LocalHash(root, path) ?? string.Empty };
                continue;
            }
            try
            {
                if (change.Kind == PullKind.Delete)
                {
                    if (change.LocalValue is not null && LocalHash(root, path) != change.LocalValue) { skipped.Add(change); continue; }
                    File.Delete(full);
                    docs.Remove(path);
                    deleted++;
                    continue;
                }
                // The file must still be what the plan saw, and the service's document the version it listed.
                if (change.Kind == PullKind.Update ? change.LocalValue is not null && LocalHash(root, path) != change.LocalValue : File.Exists(full)) { skipped.Add(change); continue; }
                var content = await session.ReadDocumentAsync(new(path), cancellationToken).ConfigureAwait(false);
                if (change.RemoteRevision is not null && content.Version != change.RemoteRevision) { skipped.Add(change); continue; }
                var bytes = content.Bytes.ToArray();
                await AtomicFile.WriteAllBytesAsync(full, bytes, () =>
                {
                    if (change.Kind == PullKind.Update && change.LocalValue is not null && LocalHash(root, path) != change.LocalValue) throw new OperationCanceledException("The file changed.");
                }, requireExisting: change.Kind == PullKind.Update, cancellationToken).ConfigureAwait(false);
                docs[path] = new BaselineDocument { Path = path, Version = content.Version, LocalHash = Hash(bytes) };
                if (change.Kind == PullKind.Create) created++; else pulled++;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                skipped.Add(change);
            }
            catch (SourceException ex)
            {
                refusal = ex.Error.Message;
                skipped.Add(change);
            }
            catch (IOException ex)
            {
                refusal = ex.Message;
                skipped.Add(change);
            }
        }

        foreach (var change in plan.Push.Where(c => c.Include))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = change.Remote.Name;
            progress?.Report(new(++done, work, $"Sending {path}"));
            try
            {
                DocumentWriteResult result;
                byte[] bytes = [];
                if (change.Kind is PushKind.DeleteKey or PushKind.DeleteTranslation)
                    result = await session.DeleteDocumentAsync(new(path), change.ExpectedRevision ?? string.Empty, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
                else
                {
                    bytes = await File.ReadAllBytesAsync(Full(root, path), cancellationToken).ConfigureAwait(false);
                    if (change.BaseValue is not null && Hash(bytes) != change.BaseValue) { errors.Add($"{path} changed since the plan was made; plan again."); failed++; continue; }
                    result = await session.WriteDocumentAsync(new(Guid.NewGuid(), new(path), bytes, change.ExpectedRevision), cancellationToken).ConfigureAwait(false);
                }
                switch (result.Outcome)
                {
                    case UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied:
                        if (change.Kind is PushKind.DeleteKey or PushKind.DeleteTranslation) docs.Remove(path);
                        else docs[path] = new BaselineDocument { Path = path, Version = result.NewVersion ?? change.ExpectedRevision ?? string.Empty, LocalHash = Hash(bytes) };
                        confirmed++;
                        break;
                    case UnitOperationOutcome.Conflict:
                        conflicts.Add(new() { Kind = SyncConflictKind.BothChanged, Field = SyncField.Value, Language = string.Empty, Remote = change.Remote, Base = Short(change.BaseValue), Local = "local file " + Short(change.BaseValue), RemoteValue = "changed at the service", RemoteRevision = result.NewVersion });
                        break;
                    case UnitOperationOutcome.Unknown or UnitOperationOutcome.None:
                        // It may have landed: read it back before deciding.
                        if (await ConfirmByReadingAsync(session, path, bytes, cancellationToken).ConfigureAwait(false) is { } version)
                        {
                            docs[path] = new BaselineDocument { Path = path, Version = version, LocalHash = Hash(bytes) };
                            confirmed++;
                        }
                        else { unknown++; errors.Add($"{path}: the service did not say whether it was saved. Plan again to check."); }
                        break;
                    default:
                        failed++;
                        errors.Add($"{path}: {result.Error?.Message ?? result.Outcome.ToString()}");
                        break;
                }
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Authentication or SourceErrorKind.Authorization)
            {
                errors.Add(ex.Error.Message);
                failed++;
                break;
            }
            catch (SourceException ex)
            {
                errors.Add($"{path}: {ex.Error.Message}");
                failed++;
            }
        }

        foreach (var (_, path) in plan.Forget) docs.Remove(path);
        file.Documents = [.. docs.Values];
        if (pulled + created + deleted > 0) file.LastPull = now;
        if (confirmed > 0) file.LastPush = now;
        file.LastPull ??= plan.Pull.Any(c => c.Kind == PullKind.Converge) ? now : null;
        store.SaveBaseline(file);
        return (new PullResult(pulled, created, deleted, skipped, refusal, plan.WorkspaceRevision), new PushResult(confirmed, failed, unknown, 0, conflicts, errors));
    }

    private static async Task<string?> ConfirmByReadingAsync(IDocumentSourceSession session, string path, byte[] wanted, CancellationToken ct)
    {
        try
        {
            var now = await session.ReadDocumentAsync(new(path), ct).ConfigureAwait(false);
            return Hash(now.Bytes.Span) == Hash(wanted) ? now.Version : null;
        }
        catch (SourceException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string Short(string? text) => string.IsNullOrEmpty(text) ? "(none)" : text.Length <= 12 ? text : text[..12];

    private static string Full(string root, string relative)
    {
        var full = Path.GetFullPath(relative, root);
        var check = Path.GetRelativePath(root, full);
        if (check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(check))
            throw new IOException($"'{relative}' is outside the project.");
        return full;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>SHA-256 of the local file, or null when it does not exist (or lies outside the project).</summary>
    private static string? LocalHash(string root, string relative)
    {
        try
        {
            var full = Full(root, relative);
            return File.Exists(full) ? Hash(File.ReadAllBytes(full)) : null;
        }
        catch (IOException) { return null; }
    }
}
