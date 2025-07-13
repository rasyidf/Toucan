using Toucan.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Toucan.Core.Contracts;

public interface IProjectFile
{
    Task<IEnumerable<TranslationItem>> LoadAsync(string folder);
    void CreateLanguage(string folder, string language);
} 
