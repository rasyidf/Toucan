using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Optional, additive declaration of settings actually consumed by an exact resource writer.</summary>
public interface IResourceOptionsProvider
{
    IReadOnlySet<string> SupportedOptions(ResourceDescriptor resource);
}

public static class ResourceOptionFields
{
    public static readonly IReadOnlySet<string> Text = new HashSet<string>
        { "InputEncoding", "OutputEncoding", "Newline", "Order", "SaveEmptyTranslations" };
    public static IReadOnlySet<string> With(params string[] fields) => new HashSet<string>(Text.Concat(fields));
}

public sealed record ResourceDialectChoice(string? Id, string Label);

/// <summary>Provider-declared dialect choices for newly added or explicitly converted resources.</summary>
public interface IResourceDialectProvider
{
    IReadOnlyList<ResourceDialectChoice> Dialects { get; }
}

/// <summary>Optional declaration of entry metadata owned by the actual resource, including field removal.</summary>
public interface IResourceMetadataAuthority
{
    bool CommentsInResource { get; }
    bool OwnsMetadata(string key);
}
