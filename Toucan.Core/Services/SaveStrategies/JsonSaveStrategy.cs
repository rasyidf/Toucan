using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class JsonSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Json;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;
        foreach (var (language, list) in context.LanguageDictionary)
        {
            var dict = list.NoEmpty().ToDictionary(t => t.Namespace, t => t.Value ?? string.Empty);
            fileService.Save(path, language + ".json", dict);
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
