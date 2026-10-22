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
        return writer is not null ? writer.WriteResource(request) : new ResourceWriteResult(
            [new ResourceDiagnostic("resource.exact-write-unsupported", "This format plugin does not support exact-file writing.",
                ResourceDiagnosticSeverity.Error, request.Document.Resource.RelativePath, "format")]);
    }
}
