using System;
using System.Collections.Generic;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Registry of known translation providers and their option/secret schemas, built from the registered
/// <see cref="ITranslationProvider"/>s so plugin providers appear in provider settings. Providers without a
/// <see cref="ITranslationProvider.Definition"/> are usable but not listed.
/// </summary>
public class TranslationProviderRegistry(IEnumerable<ITranslationProvider> providers) : ITranslationProviderRegistry
{
    private readonly IReadOnlyList<ProviderDefinition> _definitions = providers
        .Select(p => p.Definition)
        .OfType<ProviderDefinition>()
        .ToList();

    public IReadOnlyList<ProviderDefinition> GetAll() => _definitions;

    public ProviderDefinition? GetByName(string name)
        => _definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}
