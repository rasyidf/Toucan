namespace Toucan.Core.Models
{
    public class SaveContext
    {
        public Dictionary<string, IEnumerable<TranslationItem>> LanguageDictionary { get; set; }

        public List<NsTreeItem> NsTreeItems { get; set; }

        public List<string> Languages { get; set; }
    }
}
