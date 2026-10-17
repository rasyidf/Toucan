using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public sealed record WorkspaceCapabilities(string FrameworkId, string FormatId, bool DocumentRead, bool DocumentWrite)
{
    public bool UsesDocumentPersistence => DocumentRead && DocumentWrite;
    public string SaveScope => UsesDocumentPersistence ? "DeclaredDocuments" : "LegacyBulk";
}

/// <summary>Explicit selections are resolved before detection. Missing capabilities never select another framework or format.</summary>
public sealed class WorkspaceCapabilityResolver(IEnumerable<IFrameworkProfile> profiles, IEnumerable<ILoadStrategy> readers, IEnumerable<ISaveStrategy> writers)
{
    public WorkspaceCapabilities Resolve(string frameworkId, string? formatId = null)
    {
        var profile = profiles.SingleOrDefault(p => p.Id == frameworkId) ?? throw new InvalidOperationException($"Framework '{frameworkId}' is unavailable.");
        var format = formatId ?? profile.DefaultFormatId;
        var reader = readers.SingleOrDefault(r => r is not IManifestLoadStrategy && FormatIds.Comparer.Equals(r.FormatId, format)) ?? throw new FormatUnavailableException(format);
        var writer = writers.SingleOrDefault(w => FormatIds.Comparer.Equals(w.FormatId, format)) ?? throw new FormatUnavailableException(format);
        return new(frameworkId, format, reader is IDocumentLoadStrategy, writer is IDocumentSaveStrategy);
    }
}
