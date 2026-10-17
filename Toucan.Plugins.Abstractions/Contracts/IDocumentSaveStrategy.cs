using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Optional format capability. Serializes declared edits in one document without discovering files or doing I/O.
/// OriginalPath is null for insertion; Translation is null for deletion. Paths contain literal segments.
/// </summary>
public interface IDocumentSaveStrategy : ISaveStrategy
{
    string SerializeDocument(string originalContent, IReadOnlyList<DocumentTranslationEdit> edits,
        CancellationToken cancellationToken = default);
}
