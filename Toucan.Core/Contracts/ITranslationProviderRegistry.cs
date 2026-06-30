using System.Collections.Generic;
using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>
/// Exposes the set of known translation providers and their configuration schemas.
/// </summary>
public interface ITranslationProviderRegistry
{
    /// <summary>All known provider definitions (built-in + custom).</summary>
    IReadOnlyList<ProviderDefinition> GetAll();

    /// <summary>Lookup a single provider definition by name.</summary>
    ProviderDefinition? GetByName(string name);
}
