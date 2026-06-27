using System.Collections.ObjectModel;

namespace Toucan;

public class TranslationEntry
{
    public string Language { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class TranslationPackage
{
    public string Name { get; set; }

    // Collection of language -> file path entries
    public ObservableCollection<TranslationEntry> Translations { get; set; }

    public TranslationPackage(string name)
    {
        Name = name;
        Translations = [];
    }

    public override string ToString()
    {
        return Name;
    }
}
