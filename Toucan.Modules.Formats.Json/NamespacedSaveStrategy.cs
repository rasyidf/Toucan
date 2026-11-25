using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.SaveStrategies;

public class NamespacedSaveStrategy(IFileService fileService) : IDocumentSaveStrategy
{
    public string SerializeDocument(string originalContent, IReadOnlyList<DocumentTranslationEdit> edits,
        CancellationToken cancellationToken = default) =>
        JsonDocumentSerializer.Serialize(originalContent, edits, cancellationToken);

    public string FormatId => FormatIds.Namespaced;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "One JSON file per namespace under locales/<lang>/",
        ["Nested keys", "multiline text", "escapes", "numbers and booleans left unedited"],
        ["Top-level keys that are not namespaces are kept in <lang>.json", "null values are dropped", "Key order is sorted on save"]);
    public string DisplayName => "JSON (namespaced / i18next)";
    public IReadOnlyList<string> FileExtensions => [];
    public string DefaultFilePath(string language) => $"{language}.json";

    public IReadOnlyList<string> LanguageFiles(string projectRoot, string language)
    {
        // Namespaces live in locales/{lang}/; {lang}.json holds top-level keys and is listed when it exists or nothing else does.
        var merged = Path.Combine(projectRoot, language + ".json");
        var localesDir = Path.Combine(projectRoot, "locales", language);
        var namespaces = Directory.Exists(localesDir) ? Directory.GetFiles(localesDir, "*.json") : [];
        return File.Exists(merged) || namespaces.Length == 0 ? [merged, .. namespaces] : namespaces;
    }

    public void Save(string path, SaveContext context)
    {
        if (context?.NsTreeItems == null || context.Languages == null) return;

        foreach (var language in context.Languages)
        {
            // Each namespace (a top-level object) goes to locales/{lang}/{ns}.json and nowhere else; top-level keys that hold a
            // value stay in {lang}.json. Writing every key to both made a reload see each one twice. The loader reads both files,
            // so whatever an older {lang}.json held is already in the tree and is written back to exactly one place here.
            var localesPath = System.IO.Path.Combine(path, "locales", language);
            var rootValues = new Dictionary<string, object>();
            foreach (var node in context.NsTreeItems.Where(n => n.Parent == null && !string.IsNullOrWhiteSpace(n.Name)))
            {
                var nodeJson = new Dictionary<string, object>();
                node.ToJson(nodeJson, language);
                if (!nodeJson.TryGetValue(node.Name, out var inner)) continue;
                if (inner is IDictionary<string, object>)
                {
                    System.IO.Directory.CreateDirectory(localesPath);
                    fileService.Save(localesPath, node.Name + ".json", inner);
                }
                else rootValues[node.Name] = inner;
            }
            if (rootValues.Count > 0 || File.Exists(System.IO.Path.Combine(path, language + ".json")))
                fileService.Save(path, language + ".json", rootValues);
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
