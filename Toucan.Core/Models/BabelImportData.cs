namespace Toucan.Core.Models;

/// <summary>Imported metadata retained in the native project; translation text stays in resource files.</summary>
public sealed class BabelImportData
{
    public string SourceFile { get; set; } = string.Empty;
    public string? JsonLayout { get; set; }
    public Dictionary<string, string> Configuration { get; set; } = [];
    public List<BabelConcept> Concepts { get; set; } = [];
}

public sealed class BabelConcept
{
    public string Package { get; set; } = "main";
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public Dictionary<string, bool> Approved { get; set; } = [];
}

/// <summary>Review state that resource formats such as JSON cannot store inline.</summary>
public sealed class TranslationState
{
    public string Language { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
}
