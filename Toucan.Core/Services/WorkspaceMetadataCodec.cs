using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

internal static class WorkspaceMetadataCodec
{
    internal const string IdentityPath = ".toucan/workspace-identities.json";
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private sealed record IdentityFile(int SchemaVersion, string FrameworkId, string FormatId, IReadOnlyList<WorkspaceEntryIdentity> Entries);
    private sealed record MetadataFile(int SchemaVersion, DocumentId DocumentId, IReadOnlyList<WorkspaceEntryMetadata> Entries);
    internal static string EntryPath(DocumentId id) => $".toucan/entries/{id.Value:N}.json";

    internal static IReadOnlyList<WorkspaceEntryIdentity> ReadIdentities(string content, string framework, string format)
    {
        var file = JsonSerializer.Deserialize<IdentityFile>(content) ?? throw new JsonException("Invalid workspace identity file.");
        if (file.SchemaVersion != 1 || file.FrameworkId != framework || !FormatIds.Comparer.Equals(file.FormatId, format) || file.Entries is null)
            throw new InvalidOperationException("Identity checkpoint schema or source capabilities do not match this workspace.");
        if (file.Entries.Any(e => e.Id.Value == Guid.Empty || e.Namespace is null || e.KeyPath is null || string.IsNullOrEmpty(e.Resource)))
            throw new JsonException("Identity checkpoint contains invalid entries.");
        return file.Entries;
    }

    internal static IReadOnlyList<WorkspaceEntryMetadata> ReadEntries(string content, DocumentId document)
    {
        var file = JsonSerializer.Deserialize<MetadataFile>(content) ?? throw new JsonException("Invalid workspace metadata file.");
        if (file.SchemaVersion != 1 || file.DocumentId != document || file.Entries is null)
            throw new InvalidOperationException("Entry metadata schema or document identity does not match.");
        if (file.Entries.Any(e => e.EntryId.Value == Guid.Empty || string.IsNullOrWhiteSpace(e.Language) || e.Comment is null || !Enum.IsDefined(e.ChangeType)))
            throw new JsonException("Entry metadata contains invalid entries.");
        return file.Entries;
    }

    internal static string WriteIdentities(string framework, string format, IEnumerable<WorkspaceEntryIdentity> entries) =>
        JsonSerializer.Serialize(new IdentityFile(1, framework, format, entries.OrderBy(e => e.Resource, StringComparer.Ordinal)
            .ThenBy(e => e.Namespace.EscapedPath, StringComparer.Ordinal).ThenBy(e => e.KeyPath.EscapedPath, StringComparer.Ordinal).ToArray()), s_json) + "\n";
    internal static string WriteEntries(DocumentId document, IEnumerable<WorkspaceEntryMetadata> entries) =>
        JsonSerializer.Serialize(new MetadataFile(1, document, entries.OrderBy(e => e.EntryId.Value).ThenBy(e => e.Language, StringComparer.Ordinal).ToArray()), s_json) + "\n";
}
