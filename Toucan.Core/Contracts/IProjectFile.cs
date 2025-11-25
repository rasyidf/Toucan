using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IProjectFile
{
    Task<IEnumerable<TranslationItem>> LoadAsync(string folder);
    void CreateLanguage(string folder, string language);
} 
