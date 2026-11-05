using System.Runtime.CompilerServices;
using Toucan.Plugins.Sources;

namespace Toucan.Sample.Connector;

/// <summary>
/// The smallest useful content-source adapter (plugin API 1.3): it reads and writes translations of an in-memory service. Toucan
/// owns everything around it: accounts, mapping, sync state, conflict decisions, the Sources panel and the CLI.
/// </summary>
public sealed class MemorySourceAdapter(MemoryService service) : IContentSourceAdapter
{
    public const string Id = "sample.connector.memory";
    public const string ResourceId = "sample";

    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = Id,
        DisplayName = "Sample: in-memory service",
        // There is no network: a loopback address needs no entry in the manifest's "network" hosts.
        DefaultBaseUrl = new Uri("http://localhost/"),
        AuthMethods = [new ApiTokenAuth("token", "Access token")],
        Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.ConditionalWrites,
    };

    // Registration and opening touch no network. The session is created when the host first needs it and disposed with the connection.
    public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IContentSourceSession>(new Session(service));

    private sealed class Session(MemoryService service) : IUnitSourceSession
    {
        public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) => Task.FromResult(SourceTestResult.Ok("The in-memory service is always there."));

        public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken)
        {
            var languages = service.Units.Select(u => u.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(l => new RemoteLanguage(l)).ToList();
            return Task.FromResult(new SourceProjectInfo("sample", "Sample project", null, languages, [new RemoteResource(ResourceId, "All strings")],
                SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.ConditionalWrites));
        }

        // One page is the whole listing. A real service pages: set IsComplete only on the last page.
        public async IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var units = service.Units
                .Where(u => request.Languages is not { Count: > 0 } || request.Languages.Contains(u.Language))
                .Select(u => new RemoteUnit(new RemoteKey(ResourceId, u.Key), u.Language, u.Value) { Revision = u.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                .ToList();
            yield return new RemoteUnitPage(units, ContinuationToken: null, IsComplete: true);
        }

        // One result per operation, in order. A refusal is a result, never an exception.
        public Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken)
        {
            var results = new List<UnitOperationResult>();
            foreach (var operation in batch.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(operation switch
                {
                    PutValue put => Write(put.OperationId, put.Key.Name, put.Language, put.Value, ParseRevision(put.ExpectedRevision), expectNew: false),
                    CreateRemoteKey create => CreateKey(create),
                    _ => new UnitOperationResult(operation.OperationId, UnitOperationOutcome.Refused)
                        { Error = new SourceError(SourceErrorKind.Unsupported, "This sample service only stores values.") },
                });
            }
            return Task.FromResult(new UnitWriteResult(results));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private UnitOperationResult CreateKey(CreateRemoteKey create)
        {
            UnitOperationResult? last = null;
            foreach (var (language, value) in create.Values)
            {
                last = Write(create.OperationId, create.Key.Name, language, value, expectedRevision: null, expectNew: true);
                if (last.Outcome is not (UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied)) return last;
            }
            return last ?? new UnitOperationResult(create.OperationId, UnitOperationOutcome.Refused);
        }

        private UnitOperationResult Write(Guid operationId, string key, string language, string value, long? expectedRevision, bool expectNew)
        {
            var already = service.WasApplied(operationId);
            var revision = service.TryWrite(operationId, key, language, value, expectNew ? null : expectedRevision);
            if (revision is null)
            {
                var current = service.Get(key, language);
                return new UnitOperationResult(operationId, UnitOperationOutcome.Conflict)
                {
                    CurrentRemote = current is null ? null : new RemoteUnit(new RemoteKey(ResourceId, current.Key), current.Language, current.Value)
                        { Revision = current.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                };
            }
            return new UnitOperationResult(operationId, already ? UnitOperationOutcome.AlreadyApplied : UnitOperationOutcome.Applied)
                { NewRevision = revision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        private static long? ParseRevision(string? revision) =>
            long.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
