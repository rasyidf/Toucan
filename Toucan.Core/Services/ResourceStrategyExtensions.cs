using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class ResourceStrategyExtensions
{
    public static ResourceReadResult ReadResource(this ITranslationStrategyFactory factory, ResourceReadRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var reader = factory.GetResourceLoadStrategy(request.Resource.FormatId);
        return reader is not null ? reader.ReadResource(request) : new ResourceReadResult([], null,
            [new ResourceDiagnostic("resource.exact-read-unsupported", "This format plugin does not support exact-file loading.",
                ResourceDiagnosticSeverity.Error, request.Resource.RelativePath, "format")]);
    }

    public static ResourceWriteResult WriteResource(this ITranslationStrategyFactory factory, ResourceWriteRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var writer = factory.GetResourceSaveStrategy(request.Document.Resource.FormatId);
        if (writer is null) return new ResourceWriteResult(
            [new ResourceDiagnostic("resource.exact-write-unsupported", "This format plugin does not support exact-file writing.",
                ResourceDiagnosticSeverity.Error, request.Document.Resource.RelativePath, "format")]);
        try
        {
            var resource = request.Document.Resource;
            var reader = factory.GetResourceLoadStrategy(resource.FormatId);
            var permissions = resource.Ownership != ResourceOwnership.User ? ResourcePermissions.Generated
                : reader is Toucan.Core.Contracts.IResourceCapabilities capabilities ? capabilities.Permissions(resource) : ResourcePermissions.Editable;
            if (!permissions.EditKeys || !permissions.EditValues || !permissions.EditMetadata)
            {
                var baseline = factory.ReadResource(new(request.ProjectRoot, resource, request.ProjectOptions, request.CancellationToken));
                var old = baseline.Entries.ToDictionary(i => i.Identity!.KeyToken);
                if (baseline.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error) ||
                    !permissions.EditKeys && (old.Count != request.Entries.Count || request.Entries.Any(i => i.Identity == null || !old.ContainsKey(i.Identity.KeyToken))) ||
                    request.Entries.Any(i => i.Identity != null && old.TryGetValue(i.Identity.KeyToken, out var saved) &&
                        (!permissions.EditValues && saved.Value != i.Value || !permissions.EditMetadata && (saved.Comment != i.Comment || saved.IsApproved != i.IsApproved))))
                    throw new InvalidOperationException(permissions.Reason ?? "Resource capabilities forbid this change.");
                if (!permissions.EditKeys && !permissions.EditValues && !permissions.EditMetadata) return new([]);
            }
            return writer.WriteResource(request);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.EncoderFallbackException or InvalidOperationException)
        {
            return new([new ResourceDiagnostic("resource.write-invalid", ex.Message, ResourceDiagnosticSeverity.Error,
                request.Document.Resource.RelativePath, "content/options")]);
        }
    }
}
