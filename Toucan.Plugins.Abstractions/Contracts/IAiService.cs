using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>
/// AI Integration: the app-wide switch, the configured AI service and the editable prompts of each feature.
/// Every AI request in Toucan goes through here, so turning AI off stops all of them.
/// </summary>
public interface IAiService
{
    /// <summary>The app-wide switch. When off, <see cref="CompleteAsync"/> throws and no text leaves the machine.</summary>
    bool IsEnabled { get; }

    /// <summary>True when AI is on and the feature itself is not turned off.</summary>
    bool IsFeatureEnabled(string featureId);

    AiStatus GetStatus();

    /// <summary>
    /// Renders the feature's prompt, sends it with <see cref="AiRequest.Input"/> and returns the reply text.
    /// Throws <see cref="AiUnavailableException"/> when AI is off or not set up, <see cref="AiRequestException"/> when the service fails.
    /// </summary>
    Task<string> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}

/// <summary>AI cannot run: it is turned off, the feature is off, or the service has no API key. The message says which, for the user.</summary>
public sealed class AiUnavailableException : InvalidOperationException
{
    public AiUnavailableException() { }
    public AiUnavailableException(string message) : base(message) { }
    public AiUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The AI service answered with an error, e.g. "HTTP 401 Unauthorized: invalid x-api-key".</summary>
public sealed class AiRequestException : Exception
{
    public AiRequestException() { }
    public AiRequestException(string message) : base(message) { }
    public AiRequestException(string message, Exception innerException) : base(message, innerException) { }
}
