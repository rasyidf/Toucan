using OPEdit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OPEdit.Core.Contracts;

public interface IProjectFile
{
    Task<IEnumerable<TranslationItem>> LoadAsync(string folder);
    void CreateLanguage(string folder, string language);
}


