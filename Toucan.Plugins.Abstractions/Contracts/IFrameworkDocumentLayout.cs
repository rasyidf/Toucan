using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>Optional framework capability for explicit cross-language document grouping.</summary>
public interface IFrameworkDocumentLayout : IFrameworkProfile
{
    /// <summary>
    /// Describes a proposed document using discovery conventions, without I/O.
    /// Null means this layout cannot guarantee the destination will be discovered on reopen.
    /// </summary>
    DiscoveredFile? DescribeDestination(string rootFolder, string relativePath) => null;

    FrameworkDocumentLayout GetDocumentLayout(DiscoveredFile file);
}
