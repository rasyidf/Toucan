using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Core.Models;

public enum ResourceLayout { Literal, Dotted, Nested }
public enum ResourceArrayPolicy { Preserve, Disabled, ContiguousIndexes }
public enum ResourceIndent { None, Space2, Space4, Tab }
public enum ResourceUnicodeEscapes { Preserve, Always, Minimal }
public enum ResourceOrder { Preserve, Alphabetical }
public enum ResourceNewline { Lf, CrLf }

/// <summary>Null means inherit. Resolution never changes any of the input layers.</summary>
public sealed record ResourceOptions
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    public ResourceLayout? Layout { get; init; }
    public string? Delimiter { get; init; }
    public ResourceArrayPolicy? Arrays { get; init; }
    public ResourceIndent? Indent { get; init; }
    public ResourceOrder? Order { get; init; }
    public ResourceUnicodeEscapes? UnicodeEscapes { get; init; }
    public string? InputEncoding { get; init; }
    public string? OutputEncoding { get; init; }
    public ResourceNewline? Newline { get; init; }
    public bool? SaveEmptyTranslations { get; init; }

    public static ResourceOptions Resolve(ResourceOptions? resource, ResourceOptions? project,
        ResourceOptions? detected, ResourceOptions? defaults)
    {
        resource ??= new(); project ??= new(); detected ??= new(); defaults ??= new();
        return new ResourceOptions
        {
            Layout = resource.Layout ?? project.Layout ?? detected.Layout ?? defaults.Layout,
            Delimiter = resource.Delimiter ?? project.Delimiter ?? detected.Delimiter ?? defaults.Delimiter,
            Arrays = resource.Arrays ?? project.Arrays ?? detected.Arrays ?? defaults.Arrays,
            Indent = resource.Indent ?? project.Indent ?? detected.Indent ?? defaults.Indent,
            UnicodeEscapes = resource.UnicodeEscapes ?? project.UnicodeEscapes ?? detected.UnicodeEscapes ?? defaults.UnicodeEscapes,
            Order = resource.Order ?? project.Order ?? detected.Order ?? defaults.Order,
            InputEncoding = resource.InputEncoding ?? project.InputEncoding ?? detected.InputEncoding ?? defaults.InputEncoding,
            OutputEncoding = resource.OutputEncoding ?? project.OutputEncoding ?? detected.OutputEncoding ?? defaults.OutputEncoding,
            Newline = resource.Newline ?? project.Newline ?? detected.Newline ?? defaults.Newline,
            SaveEmptyTranslations = resource.SaveEmptyTranslations ?? project.SaveEmptyTranslations ?? detected.SaveEmptyTranslations ?? defaults.SaveEmptyTranslations,
            ExtensionData = MergeExtensions(defaults, detected, project, resource)
        };
    }

    private static Dictionary<string, JsonElement> MergeExtensions(params ResourceOptions[] layers)
    {
        var data = new Dictionary<string, JsonElement>();
        foreach (var layer in layers)
            foreach (var pair in layer.ExtensionData ?? []) data[pair.Key] = pair.Value;
        return data;
    }
}

/// <summary>One exact file. Id remains unchanged if its destination is moved.</summary>
public sealed record ResourceDescriptor
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Package { get; init; } = "main";
    public string Language { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FormatId { get; init; } = string.Empty;
    public string? Dialect { get; init; }
    public ResourceOwnership Ownership { get; init; }
    public string SelectorPrefix { get; init; } = string.Empty;
    public ResourceOptions Options { get; init; } = new();
}

/// <summary>One copy per document, including documents without editable entries.</summary>
public sealed class ResourceDocumentState
{
    public ResourceDescriptor Resource { get; set; } = new();
    public ResourceOptions DetectedOptions { get; set; } = new();
    public Dictionary<string, JsonElement> Data { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CompatibilityEnvelope
{
    public int Version { get; set; } = 1;
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Data { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public enum ResourceDiagnosticSeverity { Information, Warning, Error }
public sealed record ResourceDiagnostic(string Code, string Message, ResourceDiagnosticSeverity Severity,
    string? RelativePath = null, string? Field = null);

public sealed record ResourceReadRequest(string ProjectRoot, ResourceDescriptor Resource,
    ResourceOptions? ProjectOptions = null, CancellationToken CancellationToken = default);
public sealed record ResourceReadResult(IReadOnlyList<TranslationItem> Entries, ResourceDocumentState? Document,
    IReadOnlyList<ResourceDiagnostic> Diagnostics);
public sealed record ResourceWriteRequest(string ProjectRoot, ResourceDocumentState Document,
    IReadOnlyList<TranslationItem> Entries, ResourceOptions? ProjectOptions = null,
    CancellationToken CancellationToken = default);
public sealed record ResourceWriteResult(IReadOnlyList<ResourceDiagnostic> Diagnostics);
