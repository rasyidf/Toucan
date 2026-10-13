using System.Collections.Concurrent;

namespace Toucan.Sample.Connector;

/// <summary>A translation as the service holds it.</summary>
public sealed record RemoteUnit(string Key, string Language, string Text, long Revision);

/// <summary>What the connector needs from a service. A real connector implements this over HTTP.</summary>
public interface IRemoteSource
{
    Task<IReadOnlyList<RemoteUnit>> PullAsync(string workspaceId, CancellationToken cancellationToken);

    /// <summary>Writes a unit; refuses (returns false) when <paramref name="expectedRevision"/> is no longer the service's.</summary>
    Task<bool> PushAsync(string workspaceId, RemoteUnit unit, long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>An in-memory "server" shared by every connection, so the sample works without a network.</summary>
public sealed class InMemoryRemote : IRemoteSource
{
    public static InMemoryRemote Shared { get; } = new();

    private readonly ConcurrentDictionary<(string Workspace, string Key, string Language), RemoteUnit> _units = new();

    public void Seed(string workspaceId, params RemoteUnit[] units)
    {
        foreach (var u in units) _units[(workspaceId, u.Key, u.Language)] = u;
    }

    public RemoteUnit? Get(string workspaceId, string key, string language) =>
        _units.TryGetValue((workspaceId, key, language), out var u) ? u : null;

    public Task<IReadOnlyList<RemoteUnit>> PullAsync(string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RemoteUnit>>([.. _units.Where(p => p.Key.Workspace == workspaceId).Select(p => p.Value)]);
    }

    public Task<bool> PushAsync(string workspaceId, RemoteUnit unit, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var slot = (workspaceId, unit.Key, unit.Language);
        var current = _units.TryGetValue(slot, out var u) ? u.Revision : 0;
        if (current != expectedRevision) return Task.FromResult(false);
        _units[slot] = unit with { Revision = current + 1 };
        return Task.FromResult(true);
    }
}
