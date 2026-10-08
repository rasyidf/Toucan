using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.SaveStrategies;

public class NamespacedSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Namespaced;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "One JSON file per namespace under locales/<lang>/",
        ["Nested keys", "multiline text", "escapes", "numbers and booleans left unedited"],
        ["Writes both <lang>.json and locales/<lang>/<ns>.json, so a reload sees each key twice", "null values are dropped", "Key order is sorted on save"]);
    public string DisplayName => "JSON (namespaced / i18next)";
    public IReadOnlyList<string> FileExtensions => [];
    public string DefaultFilePath(string language) => $"{language}.json";

    public IReadOnlyList<string> LanguageFiles(string projectRoot, string language)
    {
        var files = new List<string> { Path.Combine(projectRoot, language + ".json") };
        // Namespaced also writes to locales/{lang}/
        var localesDir = Path.Combine(projectRoot, "locales", language);
        if (Directory.Exists(localesDir)) files.AddRange(Directory.GetFiles(localesDir, "*.json"));
        return files;
    }

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
