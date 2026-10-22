namespace Toucan.Core.Services;

/// <summary>Imported metadata retained in the native project; translation text stays in resource files.</summary>
internal sealed class ImportedProjectMetadata
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string? JsonLayout { get; set; }
    public Dictionary<string, string> Configuration { get; set; } = [];
    public List<ImportedConcept> Concepts { get; set; } = [];
}

internal sealed class ImportedConcept
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
    public string Package { get; set; } = "main";
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public Dictionary<string, bool> Approved { get; set; } = [];
}

