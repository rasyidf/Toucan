using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface IProjectService
    {
        void CreateLanguage(string folder, string language, SaveStyles style = SaveStyles.Json);
        void CreateProject(string folder, IEnumerable<string> languages, SaveStyles style = SaveStyles.Json, bool createManifest = false);
        List<TranslationItem> Load(string folder);
        void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);
    }
}
