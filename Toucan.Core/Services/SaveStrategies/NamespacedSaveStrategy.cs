using System.Collections.Generic;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.SaveStrategies;

public class NamespacedSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Namespaced;

    private readonly IFileService _fileService;
    public NamespacedSaveStrategy(IFileService fileService)
    {
        _fileService = fileService;
    }

    public void Save(string path, SaveContext context)
    {
        if (context?.NsTreeItems == null || context.Languages == null) return;

        foreach (string language in context.Languages)
        {
            Dictionary<string, dynamic> dyn = new();

            for (int i = 0; i < context.NsTreeItems.Count; i++)
            {
                context.NsTreeItems[i].ToJson(dyn, language);
            }

            // use IFileService to persist object structure
            _fileService.Save(path, language + ".json", dyn);
        }
    }
}
