using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class JsonSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Json;

    private readonly IFileService _fileService;

    public JsonSaveStrategy(IFileService fileService)
    {
        _fileService = fileService;
    }

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var kv in context.LanguageDictionary)
        {
            var language = kv.Key;
            var list = kv.Value;
            var dict = new Dictionary<string, string>();
            foreach (var t in list.NoEmpty())
            {
                dict[t.Namespace] = t.Value ?? string.Empty;
            }

            _fileService.Save(path, language + ".json", dict);
        }
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context));
    }
}
