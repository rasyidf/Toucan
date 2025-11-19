using System.Collections.Generic;
using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface IProjectService
    {
        void CreateLanguage(string folder, string language);
        List<TranslationItem> Load(string folder);
        void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);
    }
}
