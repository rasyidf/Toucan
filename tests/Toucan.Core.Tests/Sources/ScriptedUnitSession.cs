using System.Runtime.CompilerServices;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Tests.Sources;

/// <summary>A unit session over a dictionary, with switches for the failures a real service has.</summary>
internal sealed class ScriptedUnitSession : IUnitSourceSession, ISupportsAsyncJobs
{
    private readonly Dictionary<(string Name, string Language), RemoteUnit> _units = [];
    private readonly Dictionary<Guid, UnitOperationResult> _applied = [];
    private readonly Dictionary<string, SourceJobStatus> _jobs = [];
    private int _counter;

    public int WriteCalls { get; private set; }
    public List<Guid> SentOperationIds { get; } = [];
    public int AppliedCount => _applied.Count;

    /// <summary>The next write throws this after applying it (a timeout after commit).</summary>
    public Exception? ThrowAfterApplying { get; set; }

    /// <summary>The next write throws this without applying anything.</summary>
    public Exception? ThrowBeforeApplying { get; set; }

    /// <summary>Called inside the next write, before anything is applied; use it to cancel or edit the service.</summary>
    public Action? OnWrite { get; set; }

    /// <summary>Reads end without a complete page.</summary>
    public bool ReadsAreIncomplete { get; set; }

    /// <summary>Writes are accepted for an asynchronous job that ends after this many polls.</summary>
    public int? AsyncJobPolls { get; set; }

    public int MaxBatchSize { get; set; } = 100;

    public IReadOnlyDictionary<(string Name, string Language), RemoteUnit> Units => _units;

    public RemoteUnit Put(string name, string language, string value, string resource = "default")
    {
        var unit = new RemoteUnit(new RemoteKey(resource, name, "id-" + name), language, value) { Revision = (++_counter).ToString() };
        _units[(name, language)] = unit;
        return unit;
    }

    public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) => Task.FromResult(SourceTestResult.Ok());

    public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SourceProjectInfo("p", "P", "en", [], [], SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.DeleteKeys | SourceCapabilities.ConditionalWrites));

    public async IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new([.. _units.Values], null, !ReadsAreIncomplete);
    }

    public async Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken)
    {
        await Task.Yield();
        WriteCalls++;
        SentOperationIds.AddRange(batch.Operations.Select(o => o.OperationId));
        var hook = OnWrite;
        OnWrite = null;
        hook?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowBeforeApplying is { } before) { ThrowBeforeApplying = null; throw before; }
        var results = batch.Operations.Select(Apply).ToList();
        if (ThrowAfterApplying is { } after) { ThrowAfterApplying = null; throw after; }
        if (AsyncJobPolls is { } polls)
        {
            var job = "job-" + _jobs.Count;
            _jobs[job] = new SourceJobStatus(SourceJobState.Succeeded, results);
            _pollsLeft[job] = polls;
            return new UnitWriteResult([.. results.Select(r => r with { Outcome = UnitOperationOutcome.Pending })], new SourceJobHandle(job, TimeSpan.FromMilliseconds(1)));
        }
        return new UnitWriteResult(results);
    }

    private readonly Dictionary<string, int> _pollsLeft = [];

    public Task<SourceJobStatus> GetJobAsync(SourceJobHandle job, CancellationToken cancellationToken)
    {
        if (_pollsLeft.TryGetValue(job.Id, out var left) && left > 0) { _pollsLeft[job.Id] = left - 1; return Task.FromResult(new SourceJobStatus(SourceJobState.Running)); }
        return Task.FromResult(_jobs[job.Id]);
    }

    private UnitOperationResult Apply(UnitWriteOperation op)
    {
        if (_applied.TryGetValue(op.OperationId, out var earlier)) return earlier with { Outcome = UnitOperationOutcome.AlreadyApplied };
        UnitOperationResult result;
        switch (op)
        {
            case PutValue put:
            {
                if (!_units.TryGetValue((put.Key.Name, put.Language), out var current)) { result = new(op.OperationId, UnitOperationOutcome.NotFound); break; }
                if (put.ExpectedRevision is not null && put.ExpectedRevision != current.Revision) { result = new(op.OperationId, UnitOperationOutcome.Conflict) { CurrentRemote = current }; break; }
                var written = Put(put.Key.Name, put.Language, put.Value);
                result = new(op.OperationId, UnitOperationOutcome.Applied) { NewRevision = written.Revision };
                break;
            }
            case CreateRemoteKey create:
            {
                if (_units.Keys.Any(k => k.Name == create.Key.Name)) { result = new(op.OperationId, UnitOperationOutcome.Conflict); break; }
                string? revision = null;
                foreach (var (language, value) in create.Values) revision = Put(create.Key.Name, language, value).Revision;
                result = new(op.OperationId, UnitOperationOutcome.Applied) { NewRevision = revision, AssignedRemoteId = "id-" + create.Key.Name };
                break;
            }
            case DeleteRemoteKey delete:
                foreach (var k in _units.Keys.Where(k => k.Name == delete.Key.Name).ToList()) _units.Remove(k);
                result = new(op.OperationId, UnitOperationOutcome.Applied);
                break;
            case RenameRemoteKey rename:
                foreach (var k in _units.Keys.Where(k => k.Name == rename.Key.Name).ToList())
                {
                    var unit = _units[k];
                    _units.Remove(k);
                    _units[(rename.NewName, k.Language)] = unit with { Key = unit.Key with { Name = rename.NewName } };
                }
                result = new(op.OperationId, UnitOperationOutcome.Applied);
                break;
            default:
                result = new(op.OperationId, UnitOperationOutcome.Refused) { Error = new SourceError(SourceErrorKind.Unsupported, "no") };
                break;
        }
        if (result.Outcome == UnitOperationOutcome.Applied) _applied[op.OperationId] = result;
        return result;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
