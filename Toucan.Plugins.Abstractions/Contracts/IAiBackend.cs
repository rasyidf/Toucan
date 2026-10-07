using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>
/// Talks to one AI service's chat API. Backends only move text: which prompt to send, which model and key to use, and
/// whether AI is turned on at all are decided by <see cref="IAiService"/>.
/// </summary>
public interface IAiBackend
{
    AiBackendDefinition Definition { get; }

    /// <summary>Sends one completion and returns the reply text. Throws <see cref="AiRequestException"/> when the service answers with an error.</summary>
    Task<string> CompleteAsync(AiCompletionRequest request, AiEndpoint endpoint, CancellationToken cancellationToken = default);
}
