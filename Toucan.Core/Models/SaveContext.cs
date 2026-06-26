namespace Toucan.Core.Models;

public class SaveContext
{
    public required Dictionary<string, IEnumerable<TranslationItem>> LanguageDictionary { get; set; }
    public required List<NsTreeItem> NsTreeItems { get; set; }
    public required List<string> Languages { get; set; }
}
