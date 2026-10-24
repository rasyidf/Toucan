using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Contracts;

/// <summary>Optional companion: old framework profiles need not implement it.</summary>
public interface IFrameworkFeatures
{
    string ScannerId { get; }
    ConfigSchema Settings { get; }
    ResourceDescriptor Describe(DiscoveredFile file) => new()
    {
        Id = file.RelativePath.Replace('\\', '/'), RelativePath = file.RelativePath.Replace('\\', '/'), Language = file.Language,
        Package = file.Package ?? "main", FormatId = ((IFrameworkProfile)this).DefaultFormatId, Dialect = ((IFrameworkProfile)this).Id
    };
}

/// <summary>Optional file-provider companion. Unknown legacy formats keep their existing editing behavior.</summary>
public interface IResourceCapabilities
{
    ResourcePermissions Permissions(ResourceDescriptor resource);
}
