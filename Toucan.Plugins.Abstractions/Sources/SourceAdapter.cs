namespace Toucan.Plugins.Sources;

/// <summary>
/// A content source: a service outside the project that holds translations, such as a translation management system, a
/// string API or a file store. The adapter only translates between that service and Toucan; the host owns accounts,
/// credentials, connections, sync state, merge decisions and all UI (plugin API 1.3, capability <c>sources</c>).
/// </summary>
/// <remarks>
/// Register with <see cref="IPluginContext.AddSourceAdapter"/>. Registration must not touch the network. The adapter is
/// shared by every connection, so keep per-connection state in the session it returns.
/// </remarks>
public interface IContentSourceAdapter
{
    SourceAdapterDescriptor Descriptor { get; }

    /// <summary>
    /// Opens a session for one connection. Called inside the connection's scope, when the host first needs it (a pull, a
    /// push, a test), not when the project opens. May use the network. Implement <see cref="IUnitSourceSession"/>,
    /// <see cref="IDocumentSourceSession"/> or both on the returned object.
    /// </summary>
    Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken);
}

/// <summary>What an adapter is and can do, readable without calling it.</summary>
public sealed record SourceAdapterDescriptor
{
    /// <summary>Stable ID that starts with the plugin ID and a dot, e.g. <c>acme.lokalise</c>. Saved in project files.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Name of an icon from the host's icon set.</summary>
    public string Icon { get; init; } = "Cloud";

    /// <summary>The service's address when there is one public instance. Its host must be in the manifest's <c>network</c> hosts.</summary>
    public Uri? DefaultBaseUrl { get; init; }

    /// <summary>Whether an account may use another address (self-hosted servers). Needs <c>network.userDefinedHosts</c>.</summary>
    public bool AllowsCustomBaseUrl { get; init; }

    /// <summary>How users sign in, in order of preference. At least one.</summary>
    public required IReadOnlyList<SourceAuthMethod> AuthMethods { get; init; }

    public required SourceCapabilities Capabilities { get; init; }

    /// <summary>The message syntax of remote values, for validation.</summary>
    public SourceValueModel ValueModel { get; init; } = SourceValueModel.PlainText;

    /// <summary>Extra non-secret settings per connection (a branch, a file filter). Rendered by the host.</summary>
    public ConfigSchema? ConnectionSettings { get; init; }

    /// <summary>The content-source contract version the adapter was written for. The host disables what a newer one adds.</summary>
    public int ContractVersion { get; init; } = SourceContract.Version;
}

/// <summary>Versioning of the content-source contract, separate from the plugin API minor version.</summary>
public static class SourceContract
{
    /// <summary>The newest content-source contract this build understands.</summary>
    public const int Version = 1;
}

/// <summary>
/// What an adapter supports. The host never sends an operation outside these, or outside the connection's permissions
/// (<see cref="SourceProjectInfo.Permissions"/>).
/// </summary>
[Flags]
public enum SourceCapabilities
{
    None = 0,

    /// <summary>Reads units; the session implements <see cref="IUnitSourceSession"/>.</summary>
    ReadUnits = 1 << 0,
    WriteValues = 1 << 1,
    CreateKeys = 1 << 2,
    RenameKeys = 1 << 3,
    DeleteKeys = 1 << 4,
    DeleteTranslations = 1 << 5,

    /// <summary>Writes can be made conditional on a unit's or document's revision.</summary>
    ConditionalWrites = 1 << 6,

    /// <summary>Reads documents; the session implements <see cref="IDocumentSourceSession"/>.</summary>
    ReadDocuments = 1 << 7,
    WriteDocuments = 1 << 8,

    /// <summary>The session implements <see cref="ISupportsDeltaReads"/>.</summary>
    DeltaReads = 1 << 9,
    ReviewStatus = 1 << 10,
    Comments = 1 << 11,

    /// <summary>Descriptions, length limits, tags and screenshots; lazily through <see cref="ISupportsUnitContext"/>.</summary>
    UnitMetadata = 1 << 12,

    /// <summary>Writes may finish later; the session implements <see cref="ISupportsAsyncJobs"/>.</summary>
    AsyncJobs = 1 << 13,

    /// <summary>The session implements <see cref="ISupportsChangeFeed"/>.</summary>
    ChangeFeed = 1 << 14,

    /// <summary>The adapter implements <see cref="ISupportsProjectDiscovery"/>.</summary>
    ProjectDiscovery = 1 << 15,
    Plurals = 1 << 16,
}

/// <summary>The syntax of values the service stores.</summary>
public enum SourceValueModel
{
    PlainText,

    /// <summary>ICU MessageFormat.</summary>
    Icu,

    /// <summary>A platform's own syntax (printf, .NET composite format); placeholders are not converted.</summary>
    Platform,
}

/// <summary>Limits the service publishes, used for batching and rate limiting. Null means unknown.</summary>
public sealed record SourceLimits(int? MaxBatchSize = null, int? MaxConcurrency = null, int? RequestsPerMinute = null);
