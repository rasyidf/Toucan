using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Executes local document plans with optimistic version checks and declared-target recovery. Stateless between saves.</summary>
public sealed class WorkspaceDocumentSaveExecutor(IWorkspaceDocumentStore store, IEnumerable<ILoadStrategy> readers)
{
    private sealed record WriteTarget(string RelativePath, WorkspaceDocumentSaveOperation Operation, string? ExpectedVersion, byte[]? Bytes, string? Version);

    public async Task<WorkspaceDocumentSaveReceipt> ExecuteAsync(WorkspaceDocumentSavePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = new Dictionary<DocumentId, byte[]>();
        var versions = new Dictionary<DocumentId, string?>();
        var units = plan.Units.ToDictionary(u => (u.EntryId, u.Language));
        foreach (var target in plan.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalWorkspaceDocumentStore.SafePath(plan.Root, target.RelativePath);
            if (target.Operation == WorkspaceDocumentSaveOperation.Delete) { versions.Add(target.DocumentId, null); continue; }
            var reader = readers.SingleOrDefault(r => r is not IManifestLoadStrategy && FormatIds.Comparer.Equals(r.FormatId, target.FormatId))
                ?? throw new FormatUnavailableException(target.FormatId);
            if (reader is not IDocumentLoadStrategy parser)
                throw new NotSupportedException($"Format '{target.FormatId}' does not support document-aware reads.");
            var parsed = parser.LoadDocument(target.Content!, cancellationToken).ToDictionary(u => u.KeyPath);
            var expected = plan.Units.Where(u => u.Binding.DocumentId == target.DocumentId).ToList();
            if (parsed.Count != expected.Count || expected.Any(u => !parsed.TryGetValue(u.Binding.Location, out var actual) || actual.Value != u.Value))
                throw new InvalidOperationException("Serialized document does not round-trip its declared translations.");
            foreach (var unit in expected) units[(unit.EntryId, unit.Language)] = unit with { RawScalar = parsed[unit.Binding.Location].RawScalar };
            var encoding = Encoding.GetEncoding(target.TextFormat.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var bytes = target.TextFormat.HasBom ? AtomicFile.GetBytes(target.Content!, encoding) : encoding.GetBytes(target.Content!);
            encoded.Add(target.DocumentId, bytes);
            versions.Add(target.DocumentId, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        var documents = plan.Documents.Select(d => versions.TryGetValue(d.Id, out var version)
            ? d with { Content = plan.Targets.Single(t => t.DocumentId == d.Id).Content!, Version = version! } : d).ToArray();
        var writes = plan.Targets.Select(t => new WriteTarget(t.RelativePath, t.Operation, t.ExpectedVersion,
            encoded.GetValueOrDefault(t.DocumentId), versions[t.DocumentId])).ToList();
        var artifacts = plan.Source.Artifacts.ToDictionary(a => a.RelativePath, StringComparer.Ordinal);
        foreach (var target in plan.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes = null;
            string? version = null;
            if (target.Content is not null)
            {
                var encoding = Encoding.GetEncoding(target.TextFormat.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                bytes = target.TextFormat.HasBom ? AtomicFile.GetBytes(target.Content, encoding) : encoding.GetBytes(target.Content);
                version = Convert.ToHexString(SHA256.HashData(bytes));
                artifacts[target.RelativePath] = new(target.RelativePath, version, target.Content, target.TextFormat);
            }
            else artifacts.Remove(target.RelativePath);
            writes.Add(new(target.RelativePath, target.Operation, target.ExpectedVersion, bytes, version));
        }
        var saved = new WorkspaceSource(plan.Root, plan.Source.FrameworkId, plan.Source.FormatId, documents, units.Values, plan.IdentityCatalog, artifacts.Values);
        if (writes.Count == 0) return new(plan, saved);

        var lockPath = LocalWorkspaceDocumentStore.SafePath(plan.Root, ".toucan/recovery/document-save.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SaveTransaction? transaction = null;
        try
        {
            foreach (var target in writes)
                await CheckVersion(target, target.ExpectedVersion, cancellationToken).ConfigureAwait(false);
            transaction = SaveTransaction.BeginDeclaredTargets(plan.Root, writes.ToDictionary(t => t.RelativePath, t => t.ExpectedVersion));
            foreach (var target in writes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Check again before recording an intent, so an untouched external change is never rolled back.
                await CheckVersion(target, target.ExpectedVersion, cancellationToken).ConfigureAwait(false);
                transaction.DeclareMutation(target.RelativePath, target.Version);
                try
                {
                    if (target.Operation == WorkspaceDocumentSaveOperation.Delete)
                        await store.DeleteAsync(plan.Root, target.RelativePath, target.ExpectedVersion!, cancellationToken).ConfigureAwait(false);
                    else
                        await store.WriteAsync(plan.Root, target.RelativePath, target.Bytes!, target.ExpectedVersion, cancellationToken).ConfigureAwait(false);
                }
                catch (WorkspaceSourceConflictException)
                {
                    transaction.CancelMutation(target.RelativePath);
                    throw;
                }
            }
            foreach (var target in writes)
                await CheckVersion(target, target.Version, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return new(plan, saved);
        }
        catch (Exception failure)
        {
            if (transaction is not null)
            {
                var rollback = transaction.RollbackOwned();
                if (!rollback.Complete) throw new WorkspaceSaveRecoveryException(failure, rollback);
            }
            throw;
        }

        async Task CheckVersion(WriteTarget target, string? expected, CancellationToken ct)
        {
            var actual = await store.VersionAsync(plan.Root, target.RelativePath, ct).ConfigureAwait(false);
            if (actual != expected) throw new WorkspaceSourceConflictException(target.RelativePath, expected, actual);
        }
    }
}
