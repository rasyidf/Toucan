using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Tests.Sources;

/// <summary>An in-memory document service with opaque versions (not content hashes), pages, and switches for faults.</summary>
internal sealed class MemoryDocumentSession : IDocumentSourceSession
{
    private readonly Dictionary<string, (byte[] Bytes, string Version)> _documents = new(StringComparer.Ordinal);
    private int _counter;

    public int PageSize { get; set; } = 2;
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public int Deletes { get; private set; }

    /// <summary>The last page claims it is not the last, as if the service dropped the rest.</summary>
    public bool ListingNeverCompletes { get; set; }

    /// <summary>Lists the first document twice.</summary>
    public bool ListsDuplicates { get; set; }

    /// <summary>Changes the document right before a write is checked, as a concurrent editor would.</summary>
    public Action<string>? BeforeWrite { get; set; }

    public IReadOnlyCollection<string> Paths => [.. _documents.Keys.Order(StringComparer.Ordinal)];

    public void Put(string path, string text) => _documents[path] = (System.Text.Encoding.UTF8.GetBytes(text), "v" + ++_counter);

    public void Remove(string path) => _documents.Remove(path);

    public string Text(string path) => System.Text.Encoding.UTF8.GetString(_documents[path].Bytes);

    public string? Version(string path) => _documents.TryGetValue(path, out var d) ? d.Version : null;

    public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) => Task.FromResult(SourceTestResult.Ok());

    public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SourceProjectInfo("memory", "Memory", null, [], [], SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments));

    public async IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var all = _documents.Where(d => request.Prefix is null || d.Key.StartsWith(request.Prefix, StringComparison.Ordinal))
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new RemoteDocumentInfo(new(d.Key), d.Value.Version, d.Value.Bytes.Length)).ToList();
        if (ListsDuplicates && all.Count > 0) all.Add(all[0]);
        for (var i = 0; i < Math.Max(all.Count, 1); i += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var last = i + PageSize >= all.Count;
            yield return new(all.Skip(i).Take(PageSize).ToList(), last ? null : "next", last && !ListingNeverCompletes);
        }
    }

    public Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken)
    {
        Reads++;
        if (!_documents.TryGetValue(document.Path, out var found))
            throw new SourceException(new SourceError(SourceErrorKind.NotFound, document.Path + " does not exist."));
        return Task.FromResult(new RemoteDocumentContent(document, found.Bytes, found.Version));
    }

    public Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken)
    {
        Writes++;
        BeforeWrite?.Invoke(write.Ref.Path);
        var current = Version(write.Ref.Path);
        if (current != write.ExpectedVersion)
            return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Conflict, Error: new SourceError(SourceErrorKind.Conflict, "changed")));
        _documents[write.Ref.Path] = (write.Bytes.ToArray(), "v" + ++_counter);
        return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Applied, _documents[write.Ref.Path].Version));
    }

    public Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken)
    {
        Deletes++;
        if (Version(document.Path) != expectedVersion)
            return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Conflict, Error: new SourceError(SourceErrorKind.Conflict, "changed")));
        _documents.Remove(document.Path);
        return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Applied));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
