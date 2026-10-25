namespace Toucan.Core.Models;

public class SaveContext
{
    public ResourceOptions ResourceOptions { get; set; } = new();
    public List<ResourceDocumentState> Documents { get; set; } = [];
    /// <summary>Keep intentional empty translations unless the project explicitly opts out.</summary>
    public bool SaveEmptyTranslations { get; set; } = true;
    public string? JsonLayout { get; set; }
    public required Dictionary<string, IEnumerable<TranslationItem>> LanguageDictionary { get; set; }
    public required List<NsTreeItem> NsTreeItems { get; set; }
    public required List<string> Languages { get; set; }
}
