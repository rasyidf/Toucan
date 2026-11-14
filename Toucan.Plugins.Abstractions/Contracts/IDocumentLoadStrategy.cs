using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Optional capability of an ILoadStrategy, registered through the existing format registration.
/// Parses one document without discovery, flattening keys, or writing files. Throws on invalid input.
/// </summary>
public interface IDocumentLoadStrategy : ILoadStrategy
{
    IReadOnlyList<DocumentTranslation> LoadDocument(string content, CancellationToken cancellationToken = default);
}
