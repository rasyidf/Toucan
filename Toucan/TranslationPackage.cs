using System.Collections.ObjectModel;

namespace Toucan;

public class TranslationEntry
{
    public string Language { get; set; }
    public string FilePath { get; set; }
}

public class TranslationPackage
{
    public string Name { get; set; }

    // Collection of language -> file path entries
    public ObservableCollection<TranslationEntry> Translations { get; set; }

    public TranslationPackage(string name)
    {
        Name = name;
        Translations = new ObservableCollection<TranslationEntry>();
    }

    public override string ToString() => Name;
}
