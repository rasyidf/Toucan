namespace Toucan.Core.Models;

/// <summary>Neutral migration result. The host creates its native project from this data.</summary>
public sealed class CompatibilityProject
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Framework { get; set; } = "";
    public string SaveFormat { get; set; } = "";
    public string PrimaryLanguage { get; set; } = "";
    public List<string> Languages { get; set; } = [];
    public List<TranslationPackage> TranslationPackages { get; set; } = [];
    public List<ResourceDocumentState> ResourceDocuments { get; set; } = [];
    public ResourceOptions ResourceOptions { get; set; } = new();
    public Dictionary<string, CompatibilityEnvelope> Compatibility { get; set; } = [];
    public bool? SaveEmptyTranslations { get; set; }
    public string? InputEncoding { get; set; }
    public string? TranslationOrder { get; set; }
    public List<string> CopyTemplates { get; set; } = [];
}
