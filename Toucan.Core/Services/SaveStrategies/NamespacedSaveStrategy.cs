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

        // For backward compatibility, continue to write a per-language single file
        foreach (string language in context.Languages)
        {
            Dictionary<string, dynamic> dyn = new();
            for (int i = 0; i < context.NsTreeItems.Count; i++)
            {
                context.NsTreeItems[i].ToJson(dyn, language);
            }
            _fileService.Save(path, language + ".json", dyn);

            // Also write per-namespace files in a locales/{lang}/ folder
            var localesPath = System.IO.Path.Combine(path, "locales", language);
            try
            {
                System.IO.Directory.CreateDirectory(localesPath);
            }
            catch { }

            // Iterate top-level namespaces (NsTreeItems with no parent) and write each as a separate file
            foreach (var node in context.NsTreeItems.Where(n => n.Parent == null))
            {
                if (string.IsNullOrWhiteSpace(node.Name)) continue;
                var nodeJson = new Dictionary<string, dynamic>();
                node.ToJson(nodeJson, language);

                // The ToJson call wraps the content under node.Name, extract inner content if present
                if (nodeJson.TryGetValue(node.Name ?? string.Empty, out var inner))
                {
                    _fileService.Save(localesPath, (node.Name ?? "" ) + ".json", inner);
                }
                else
                {
                    // Fallback to full content
                    _fileService.Save(localesPath, (node.Name ?? "" ) + ".json", nodeJson);
                }
            }
        }
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context));
    }
}
