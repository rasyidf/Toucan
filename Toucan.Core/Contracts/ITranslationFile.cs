using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface ITranslationFile
{
    void Save(IEnumerable<TranslationItem> items, string path, List<string> languages);
}
