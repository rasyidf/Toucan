using System.Collections.Generic;

namespace OPEdit.Core.Contracts;

public interface ITranslationFile
{
    void Save(IEnumerable<ITranslationItem> items, string path, List<string> languages);
}


