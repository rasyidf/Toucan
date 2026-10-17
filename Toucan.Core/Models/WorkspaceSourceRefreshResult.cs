namespace Toucan.Core.Models;

public sealed record WorkspaceSourceRefreshResult(IReadOnlyList<DocumentId> Reloaded, IReadOnlyList<DocumentId> Conflicts);
