using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.SaveStrategies;

public class NamespacedSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Namespaced;

    public void Save(string path, SaveContext context)
    {
        if (context?.NsTreeItems == null || context.Languages == null) return;

        foreach (var language in context.Languages)
        {
            // Write single merged file
            Dictionary<string, object> dyn = [];
            foreach (var item in context.NsTreeItems)
                item.ToJson(dyn, language);
            fileService.Save(path, language + ".json", dyn);

            // Also write per-namespace files in locales/{lang}/
            var localesPath = System.IO.Path.Combine(path, "locales", language);
            System.IO.Directory.CreateDirectory(localesPath);

            foreach (var node in context.NsTreeItems.Where(n => n.Parent == null && !string.IsNullOrWhiteSpace(n.Name)))
            {
                var nodeJson = new Dictionary<string, object>();
                node.ToJson(nodeJson, language);
                if (nodeJson.TryGetValue(node.Name, out var inner))
                    fileService.Save(localesPath, node.Name + ".json", inner);
                else
                    fileService.Save(localesPath, node.Name + ".json", nodeJson);
            }
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
