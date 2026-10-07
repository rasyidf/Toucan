using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Marks the load strategy that reads a project from its manifest (<c>toucan.tproj</c>) rather than by scanning
/// a folder. It reports the same <c>FormatId</c> as the plain folder loader, so the factory tells them apart
/// with this interface, never by type name or registration order.
/// </summary>
public interface IManifestLoadStrategy : ILoadStrategy;
