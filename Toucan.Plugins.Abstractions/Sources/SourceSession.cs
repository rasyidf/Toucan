using Microsoft.Extensions.Logging;

namespace Toucan.Plugins.Sources;

/// <summary>An account's context: who is signed in where. Used for project discovery before a connection exists.</summary>
public interface ISourceAccountContext
{
    string AccountId { get; }

    /// <summary>The service's address for this account.</summary>
    Uri BaseUrl { get; }

    ISourceCredentials Credentials { get; }
    ISourceHttpClientFactory Http { get; }
    IPluginDiagnostics Diagnostics { get; }
    ILogger Logger { get; }

    /// <summary>Clock for expiry and backoff, replaceable in tests.</summary>
    TimeProvider Time { get; }
}

/// <summary>What the host tells an adapter about the connection a session is for.</summary>
public interface ISourceSessionContext : ISourceAccountContext
{
    string WorkspaceId { get; }
    string ConnectionId { get; }
    SourceConnectionInfo Connection { get; }

    /// <summary>The connection's scope: cancelled when the connection closes or its project closes.</summary>
    CancellationToken Scope { get; }
}

/// <summary>Whether local files or the service is primary for a connection.</summary>
public enum SourceRole
{
    /// <summary>Local files are primary; the service is a sync target (a developer repository plus a TMS).</summary>
    Mirror,

    /// <summary>The service is primary and Toucan keeps a local cache (a translator without a repository).</summary>
    Primary,
}

/// <summary>A remote resource mapped to a project package or document family.</summary>
public sealed record SourceResourceMapping(string RemoteResourceId, string? Package);

/// <summary>A connection as the adapter sees it. Contains no secrets.</summary>
public sealed record SourceConnectionInfo
{
    public required string ConnectionId { get; init; }
    public required string AdapterId { get; init; }
    public required Uri BaseUrl { get; init; }
    public required string RemoteProjectId { get; init; }
    public SourceRole Role { get; init; } = SourceRole.Mirror;

    /// <summary>Project language → remote language code. Only mapped languages are read or written.</summary>
    public IReadOnlyDictionary<string, string> Languages { get; init; } = new Dictionary<string, string>();

    /// <summary>Remote resources in this connection; empty means every resource the project holds.</summary>
    public IReadOnlyList<SourceResourceMapping> Resources { get; init; } = [];

    /// <summary>Values of the adapter's <see cref="SourceAdapterDescriptor.ConnectionSettings"/>.</summary>
    public IReadOnlyDictionary<string, object?> Settings { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// A live session for one connection. Implement <see cref="IUnitSourceSession"/>, <see cref="IDocumentSourceSession"/> or
/// both. The session holds no sync state: baselines, journals and decisions belong to the host. Disposed when the
/// connection closes.
/// </summary>
public interface IContentSourceSession : IAsyncDisposable
{
    /// <summary>A cheap check that the credential works and the project is reachable (Test connection).</summary>
    Task<SourceTestResult> TestAsync(CancellationToken cancellationToken);

    /// <summary>Facts about the remote project: name, languages, resources, what the credential may do, limits.</summary>
    Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken);
}

/// <summary>The outcome of <see cref="IContentSourceSession.TestAsync"/>.</summary>
public sealed record SourceTestResult(bool Succeeded, string Message, SourceError? Error = null)
{
    public static SourceTestResult Ok(string message = "Connected.") => new(true, message);
    public static SourceTestResult Failed(SourceError error) => new(false, error.Message, error);
}

public sealed record RemoteLanguage(string Code, string? DisplayName = null, bool IsSource = false);

/// <summary>A remote file, namespace or bundle.</summary>
public sealed record RemoteResource(string Id, string Name, string? Path = null, string? FormatHint = null);

/// <summary>A remote project as the service describes it.</summary>
/// <param name="Permissions">What the credential may do, narrowing the adapter's capabilities for this connection.</param>
public sealed record SourceProjectInfo(string RemoteProjectId, string Name, string? SourceLanguage,
    IReadOnlyList<RemoteLanguage> Languages, IReadOnlyList<RemoteResource> Resources,
    SourceCapabilities Permissions, SourceLimits? Limits = null);
