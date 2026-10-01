namespace Toucan.Core.Models;

/// <summary>
/// The project uses a format (typically from a plugin) that has no registered strategy.
/// Thrown instead of silently falling back to another format, which could overwrite the user's files on save.
/// </summary>
public sealed class FormatUnavailableException : InvalidOperationException
{
    public string FormatId { get; }

    public FormatUnavailableException(string formatId)
        : base($"This project uses the '{formatId}' format, which is not available. Install or enable the plugin that provides it, then reopen the project.")
        => FormatId = formatId;

    public FormatUnavailableException()
        : this("unknown")
    { }

    public FormatUnavailableException(string message, Exception innerException)
        : base(message, innerException)
        => FormatId = "unknown";
}
