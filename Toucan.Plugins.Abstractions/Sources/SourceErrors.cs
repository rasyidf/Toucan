namespace Toucan.Plugins.Sources;

/// <summary>What went wrong, in terms the host can act on.</summary>
public enum SourceErrorKind
{
    /// <summary>Unclassified; treated like <see cref="Internal"/>.</summary>
    Unknown,

    /// <summary>The credential is missing, expired or revoked: the user signs in again.</summary>
    Authentication,

    /// <summary>Signed in, but not allowed: the host names the missing permission.</summary>
    Authorization,
    NotFound,
    Conflict,

    /// <summary>The service refused the content; retrying will not help.</summary>
    Validation,
    RateLimited,

    /// <summary>The service is down or overloaded; retry later.</summary>
    Unavailable,
    Network,
    Timeout,

    /// <summary>The service cannot do this at all.</summary>
    Unsupported,
    Internal,
}

/// <summary>A failure from a service. <paramref name="Message"/> is shown to users after redaction.</summary>
public sealed record SourceError(SourceErrorKind Kind, string Message)
{
    /// <summary>When the service said to try again.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>The service's own error code.</summary>
    public string? RemoteCode { get; init; }
    public Uri? HelpUrl { get; init; }

    /// <summary>Whether retrying the same request later can succeed.</summary>
    public bool IsTransient => Kind is SourceErrorKind.RateLimited or SourceErrorKind.Unavailable or SourceErrorKind.Network or SourceErrorKind.Timeout;
}

/// <summary>Thrown by reads and session calls for a <see cref="SourceError"/>.</summary>
public sealed class SourceException : Exception
{
    public SourceException() : this(new SourceError(SourceErrorKind.Unknown, "The source failed.")) { }

    public SourceException(string message) : this(new SourceError(SourceErrorKind.Unknown, message)) { }

    public SourceException(string message, Exception innerException) : this(new SourceError(SourceErrorKind.Unknown, message), innerException) { }

    public SourceException(SourceError error, Exception? innerException = null) : base(error?.Message, innerException) =>
        Error = error ?? throw new ArgumentNullException(nameof(error));

    public SourceError Error { get; }
}
