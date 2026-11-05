using System.Collections.Concurrent;

namespace Toucan.Sample.Connector;

/// <summary>A translation as the sample "service" holds it. Revisions only ever go up.</summary>
public sealed record StoredUnit(string Key, string Language, string Value, long Revision);

/// <summary>
/// An in-memory "service" shared by every connection, so the sample runs without a network. A real connector talks to an HTTP
/// API instead; everything else in this sample is what a real adapter looks like.
/// </summary>
public sealed class MemoryService
{
    public static MemoryService Shared { get; } = new();

    private readonly ConcurrentDictionary<(string Key, string Language), StoredUnit> _units = new();
    private readonly ConcurrentDictionary<Guid, long> _applied = new();
    private long _revision;

    public IReadOnlyList<StoredUnit> Units => [.. _units.Values.OrderBy(u => u.Key, StringComparer.Ordinal).ThenBy(u => u.Language, StringComparer.Ordinal)];

    public StoredUnit? Get(string key, string language) => _units.GetValueOrDefault((key, language));

    /// <summary>Seeds or edits a unit as another user would.</summary>
    public StoredUnit Put(string key, string language, string value) =>
        _units[(key, language)] = new(key, language, value, Interlocked.Increment(ref _revision));

    public void Clear()
    {
        _units.Clear();
        _applied.Clear();
    }

    /// <summary>
    /// Writes when <paramref name="expectedRevision"/> is still the unit's (null: the unit must not exist). Returns the new revision,
    /// the revision of an earlier delivery of the same operation, or null on a conflict.
    /// </summary>
    public long? TryWrite(Guid operationId, string key, string language, string value, long? expectedRevision)
    {
        if (_applied.TryGetValue(operationId, out var earlier)) return earlier;
        var slot = (key, language);
        var current = _units.TryGetValue(slot, out var unit) ? unit.Revision : (long?)null;
        if (current != expectedRevision) return null;
        var written = Put(key, language, value);
        _applied[operationId] = written.Revision;
        return written.Revision;
    }

    public bool WasApplied(Guid operationId) => _applied.ContainsKey(operationId);
}
