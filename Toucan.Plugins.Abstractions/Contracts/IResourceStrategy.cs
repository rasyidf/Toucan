using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Optional exact-file capability. Folder plugins need not implement it.</summary>
public interface IResourceLoadStrategy
{
    string FormatId { get; }
    ResourceReadResult ReadResource(ResourceReadRequest request);
}

public interface IResourceSaveStrategy
{
    string FormatId { get; }
    ResourceWriteResult WriteResource(ResourceWriteRequest request);
}

public interface IResourceDiscoveryStrategy
{
    IEnumerable<ResourceDescriptor> DiscoverResources(string projectRoot);
}
