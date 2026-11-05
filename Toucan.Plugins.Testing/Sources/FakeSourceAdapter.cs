using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Toucan.Plugins.Sources;

namespace Toucan.Plugins.Testing.Sources;

/// <summary>Deliberate mistakes a <see cref="FakeSourceAdapter"/> can make, to check that the conformance suite and the host catch them.</summary>
[Flags]
public enum FakeAdapterFlaws
{
    None = 0,

    /// <summary>Reports a read complete even when pages were dropped.</summary>
    ClaimsCompleteOnDroppedPage = 1 << 0,

    /// <summary>Returns write results in reverse order.</summary>
    ResultsOutOfOrder = 1 << 1,

    /// <summary>Sends every write with a fresh idempotency key instead of the host's.</summary>
    DropsOperationIds = 1 << 2,

    /// <summary>Writes unconditionally, ignoring expected revisions.</summary>
    IgnoresExpectedRevision = 1 << 3,

    /// <summary>Ignores cancellation of reads.</summary>
    IgnoresCancellation = 1 << 4,

    /// <summary>Reports an ambiguous timeout as a plain failure instead of <see cref="UnitOperationOutcome.Unknown"/>.</summary>
    HidesAmbiguousFailures = 1 << 5,
}

/// <summary>
/// A complete adapter over a <see cref="FakeSourceServer"/>: units, documents, delta reads, async jobs, a change feed and
/// project discovery. It is also a reference for writing a real adapter: credential refresh on an expired token, honest
/// <c>IsComplete</c>, per-operation results, and <see cref="UnitOperationOutcome.Unknown"/> for ambiguous failures.
/// </summary>
public sealed class FakeSourceAdapter : IContentSourceAdapter, ISupportsProjectDiscovery
{
    public const string DefaultId = "test.plugin.fake";

    public const SourceCapabilities AllCapabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys
        | SourceCapabilities.RenameKeys | SourceCapabilities.DeleteKeys | SourceCapabilities.DeleteTranslations | SourceCapabilities.ConditionalWrites
        | SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.DeltaReads | SourceCapabilities.ReviewStatus
        | SourceCapabilities.Comments | SourceCapabilities.AsyncJobs | SourceCapabilities.ChangeFeed | SourceCapabilities.ProjectDiscovery;

    public FakeSourceAdapter(FakeSourceServer server, string id = DefaultId, SourceCapabilities capabilities = AllCapabilities, FakeAdapterFlaws flaws = FakeAdapterFlaws.None)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        Flaws = flaws;
        Descriptor = new SourceAdapterDescriptor
        {
            Id = id,
            DisplayName = "Fake source",
            DefaultBaseUrl = new Uri("https://source.test/"),
            AuthMethods = [new ApiTokenAuth("token", "API token")],
            Capabilities = capabilities,
        };
    }

    public FakeSourceServer Server { get; }
    public FakeAdapterFlaws Flaws { get; }
    public SourceAdapterDescriptor Descriptor { get; }

    /// <summary>Sessions opened and not yet disposed.</summary>
    public int OpenSessions => _open;
    private int _open;

    public async Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        await context.Credentials.GetAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _open);
        return new Session(this, context);
    }

    public async IAsyncEnumerable<RemoteProjectSummary> ListProjectsAsync(ISourceAccountContext account, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var token = (await account.Credentials.GetAsync(cancellationToken).ConfigureAwait(false)).Value;
        var info = await Server.DescribeAsync(token, cancellationToken).ConfigureAwait(false);
        yield return new RemoteProjectSummary(info.RemoteProjectId, info.Name);
    }

    private sealed class Session(FakeSourceAdapter adapter, ISourceSessionContext context)
        : IUnitSourceSession, IDocumentSourceSession, ISupportsDeltaReads, ISupportsAsyncJobs, ISupportsChangeFeed
    {
        private FakeSourceServer Server => adapter.Server;
        private bool Has(FakeAdapterFlaws flaw) => adapter.Flaws.HasFlag(flaw);
        private int _disposed;

        /// <summary>Calls the server with the current token, refreshing once when it reports the token expired.</summary>
        private async Task<T> CallAsync<T>(Func<string, Task<T>> call, CancellationToken cancellationToken)
        {
            var credential = await context.Credentials.GetAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await call(credential.Value).ConfigureAwait(false);
            }
            catch (SourceException ex) when (ex.Error.Kind == SourceErrorKind.Authentication)
            {
                credential = await context.Credentials.RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
                return await call(credential.Value).ConfigureAwait(false);
            }
        }

        public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) =>
            CallAsync(token => Server.TestAsync(token, cancellationToken), cancellationToken);

        public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken) =>
            CallAsync(token => Server.DescribeAsync(token, cancellationToken), cancellationToken);

        public async IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var token = Has(FakeAdapterFlaws.IgnoresCancellation) ? CancellationToken.None : cancellationToken;
            var (dropAfter, duplicate) = Server.TakeReadShape();
            var next = request.ContinuationToken;
            for (var index = 0; ; index++)
            {
                var page = await CallAsync(t => Server.ReadPageAsync(t, request with { ContinuationToken = next }, token), token).ConfigureAwait(false);
                if (dropAfter is { } drop && index + 1 >= drop && !page.IsComplete)
                {
                    // The connection dropped mid-read: say so, unless this adapter is the flawed one.
                    yield return Has(FakeAdapterFlaws.ClaimsCompleteOnDroppedPage) ? page with { IsComplete = true, ContinuationToken = null } : page;
                    yield break;
                }
                yield return page;
                if (duplicate == index) yield return page;
                if (page.IsComplete || page.ContinuationToken is null) yield break;
                next = page.ContinuationToken;
            }
        }

        public async Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);
            cancellationToken.ThrowIfCancellationRequested();
            var sent = new UnitWriteBatch([.. batch.Operations.Select(Prepare)]);
            UnitWriteResult result;
            try
            {
                result = await CallAsync(t => Server.WriteAsync(t, sent, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Timeout or SourceErrorKind.Network)
            {
                // The request may have landed: only the host can find out, by reading.
                var outcome = Has(FakeAdapterFlaws.HidesAmbiguousFailures) ? UnitOperationOutcome.Failed : UnitOperationOutcome.Unknown;
                return new UnitWriteResult([.. batch.Operations.Select(o => new UnitOperationResult(o.OperationId, outcome) { Error = ex.Error })]);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.RateLimited)
            {
                return new UnitWriteResult([.. batch.Operations.Select(o => new UnitOperationResult(o.OperationId, UnitOperationOutcome.RateLimited) { Error = ex.Error })]);
            }
            catch (SourceException ex)
            {
                return new UnitWriteResult([.. batch.Operations.Select(o => new UnitOperationResult(o.OperationId, UnitOperationOutcome.Failed) { Error = ex.Error })]);
            }

            // Results carry the host's operation IDs, whatever this adapter sent.
            var results = result.Results.Select((r, i) => r with { OperationId = batch.Operations[i].OperationId }).ToList();
            if (Has(FakeAdapterFlaws.ResultsOutOfOrder)) results.Reverse();
            return result with { Results = results };
        }

        private UnitWriteOperation Prepare(UnitWriteOperation operation)
        {
            var op = Has(FakeAdapterFlaws.DropsOperationIds) ? operation with { OperationId = Guid.NewGuid() } : operation;
            if (!Has(FakeAdapterFlaws.IgnoresExpectedRevision)) return op;
            return op switch
            {
                PutValue p => p with { ExpectedRevision = null },
                PutStatus p => p with { ExpectedRevision = null },
                DeleteTranslation p => p with { ExpectedRevision = null },
                _ => op,
            };
        }

        public async IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var next = request.ContinuationToken;
            while (true)
            {
                var page = await CallAsync(t => Server.ListDocumentsAsync(t, request with { ContinuationToken = next }, cancellationToken), cancellationToken).ConfigureAwait(false);
                yield return page;
                if (page.IsComplete || page.ContinuationToken is null) yield break;
                next = page.ContinuationToken;
            }
        }

        public Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken) =>
            CallAsync(t => Server.ReadDocumentAsync(t, document, cancellationToken), cancellationToken);

        public async Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken)
        {
            try
            {
                return await CallAsync(t => Server.WriteDocumentAsync(t, write, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Timeout or SourceErrorKind.Network)
            {
                return new DocumentWriteResult(Has(FakeAdapterFlaws.HidesAmbiguousFailures) ? UnitOperationOutcome.Failed : UnitOperationOutcome.Unknown, Error: ex.Error);
            }
        }

        public Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
            CallAsync(t => Server.DeleteDocumentAsync(t, document, expectedVersion, operationId, cancellationToken), cancellationToken);

        public async IAsyncEnumerable<RemoteChangePage> ReadChangesAsync(DeltaReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return await CallAsync(t => Server.ReadChangesAsync(t, request, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        public Task<SourceJobStatus> GetJobAsync(SourceJobHandle job, CancellationToken cancellationToken) =>
            CallAsync(t => Server.GetJobAsync(t, job, cancellationToken), cancellationToken);

        public async IAsyncEnumerable<SourceChangeHint> WatchAsync(string? cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var hints = Channel.CreateBounded<SourceChangeHint>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
            void OnChanged(object? sender, SourceChangedEventArgs e) => hints.Writer.TryWrite(e.Hint);
            Server.Changed += OnChanged;
            try
            {
                await foreach (var hint in hints.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    yield return hint;
            }
            finally
            {
                Server.Changed -= OnChanged;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref adapter._open);
            return ValueTask.CompletedTask;
        }
    }
}
